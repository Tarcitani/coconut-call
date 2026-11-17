# Gera o pacote portátil do app Coconut (Windows 11 x64): zip autocontido, sem instalador (P7 pendente).
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publish-app.ps1 -Server wss://seu-host/ws
#   -Server vai só para coconut.defaults.json dentro do pacote (nunca para o repositório) e vale enquanto
#   a pessoa não tiver configurações próprias.
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^wss://[^/\s]+/ws$')]
    [string]$Server
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$name = "Coconut-win-x64"
$stage = Join-Path $repo "artifacts\app\$name"
$archive = Join-Path $repo "artifacts\app\$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# 1. App autocontido: .NET e Windows App SDK vão junto.
dotnet publish (Join-Path $repo "src\Coconut.App") -c Release -r win-x64 --self-contained true -p:DebugType=None -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

$native = "datachannel.dll", "juice.dll", "libcrypto-3-x64.dll", "libssl-3-x64.dll", "opus.dll", "srtp2.dll"
foreach ($file in @("Coconut.exe", "Coconut.pri") + $native) {
    if (-not (Test-Path (Join-Path $stage $file))) { throw "$file ausente na saída do publish" }
}

# 2. Runtime do Visual C++ ao lado do exe: as DLLs nativas (MSVC) importam MSVCP140, VCRUNTIME140 e
#    VCRUNTIME140_1, que não vêm com o Windows (o UCRT, api-ms-win-crt-*, vem). Redistribuição local
#    permitida pela Microsoft; versão do mesmo toolset que compilou os nativos.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -property installationPath
$crt = Get-ChildItem (Join-Path $vs "VC\Redist\MSVC\*\x64\Microsoft.VC14*.CRT") -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "vcruntime140_1.dll") } |
    Sort-Object { [version]$_.Parent.Parent.Name } -Descending | Select-Object -First 1
if (-not $crt) { throw "Redistribuível do VC++ não encontrado em $vs" }
foreach ($dll in "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll") { Copy-Item (Join-Path $crt.FullName $dll) $stage }

# 3. Servidor pré-configurado, LEIA-ME e licenças (GPLv3 exige a licença e o acesso ao código-fonte).
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $stage "coconut.defaults.json"), (@{ ServerAddress = $Server } | ConvertTo-Json -Compress), $utf8)
Copy-Item (Join-Path $repo "deploy\windows\LEIA-ME.txt") $stage
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENCA.txt")
$licenses = Join-Path $stage "licencas-terceiros"
New-Item -ItemType Directory -Force $licenses | Out-Null
$share = Join-Path $repo "native\vcpkg_installed\x64-windows\share"
foreach ($port in "libdatachannel", "libjuice", "libsrtp", "openssl", "opus", "usrsctp", "plog", "nlohmann-json") {
    Copy-Item (Join-Path $share "$port\copyright") (Join-Path $licenses "$port.txt")
}
[System.IO.File]::WriteAllText((Join-Path $licenses "OUTROS.txt"), @"
Também incluídos, sob as licenças dos respectivos autores:
- .NET Runtime e ASP.NET Core - MIT - https://github.com/dotnet/runtime
- Windows App SDK / WinUI 3 - Microsoft (redistribuível) - https://github.com/microsoft/WindowsAppSDK
- CommunityToolkit.Mvvm - MIT - https://github.com/CommunityToolkit/dotnet
- Microsoft Visual C++ Runtime (msvcp140, vcruntime140, vcruntime140_1) - redistribuível da Microsoft
  (Visual Studio Build Tools $($crt.Parent.Parent.Name))
"@, $utf8)

# 4. Zip (tar.exe do Windows/bsdtar escolhe o formato pela extensão).
if (Test-Path $archive) { Remove-Item $archive -Force }
$tar = Join-Path $env:SystemRoot "System32\tar.exe"
& $tar -a -cf $archive -C (Split-Path $stage) $name
if ($LASTEXITCODE -ne 0) { throw "tar falhou" }

$size = [Math]::Round((Get-Item $archive).Length / 1MB, 1)
"Pacote: $archive ($size MB)"
"SHA-256: $((Get-FileHash $archive -Algorithm SHA256).Hash)"
