# Gera o payload autocontido e o zip portátil do app Coconut (Windows 11 x64).
# Para gerar também o instalador offline, use publish-installer.ps1.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publish-app.ps1 [-Server wss://seu-host/ws] [-Version 0.1.0]
#   -Server vai só para coconut.defaults.json dentro do pacote (nunca para o repositório) e vale enquanto
#   a pessoa não tiver configurações próprias. Sem -Server (pacote público): a pessoa informa o servidor
#   em Avançado. -Version entra no nome do zip e na versão do executável.
param(
    [ValidatePattern('^(wss://[^/\s]+/ws)?$')]
    [string]$Server = "",
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = (Get-Content (Join-Path $PSScriptRoot "..\VERSION") -Raw).Trim()
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$name = if ($Server) { "Coconut-$Version-win-x64-servidor" } else { "Coconut-$Version-win-x64" }
$stage = Join-Path $repo "artifacts\app\$name"
$archive = Join-Path $repo "artifacts\app\$name.zip"

# Qualquer limpeza recursiva deve continuar estritamente dentro de artifacts\app.
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repo "artifacts\app")) + [System.IO.Path]::DirectorySeparatorChar
$stage = [System.IO.Path]::GetFullPath($stage)
if (-not $stage.StartsWith($outputRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Pasta de saída inválida: $stage" }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

# 1. App autocontido: .NET e Windows App SDK vão junto.
dotnet publish (Join-Path $repo "src\Coconut.App") -c Release -r win-x64 --self-contained true -p:DebugType=None -p:Version=$Version -p:IncludeSourceRevisionInInformationalVersion=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

$native = "datachannel.dll", "juice.dll", "libcrypto-3-x64.dll", "libssl-3-x64.dll", "opus.dll", "srtp2.dll", "Coconut.Screen.Native.dll", "libsodium.dll"
foreach ($file in @("Coconut.exe", "Coconut.pri", "Assets\Coconut.ico", "coreclr.dll", "hostfxr.dll", "Microsoft.UI.Xaml.dll") + $native) {
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
Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' | Copy-Item -Destination $stage

# 3. Servidor pré-configurado, LEIA-ME e licenças (GPLv3 exige a licença e o acesso ao código-fonte).
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($Server) {
    [System.IO.File]::WriteAllText((Join-Path $stage "coconut.defaults.json"), (@{ ServerAddress = $Server } | ConvertTo-Json -Compress), $utf8)
}
Copy-Item (Join-Path $repo "deploy\windows\LEIA-ME.txt") $stage
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENCA.txt")
[System.IO.File]::WriteAllText((Join-Path $stage "CODIGO-FONTE.txt"), "Coconut $Version`r`nhttps://github.com/Tarcitani/coconut-call/tree/v$Version`r`nCompilação e dependências: docs/distribuicao-windows.md no repositório.`r`n", $utf8)
$licenses = Join-Path $stage "licencas-terceiros"
New-Item -ItemType Directory -Force $licenses | Out-Null
$share = Join-Path $repo "native\vcpkg_installed\x64-windows\share"
foreach ($port in "libdatachannel", "libjuice", "libsrtp", "openssl", "opus", "usrsctp", "plog", "nlohmann-json") {
    Copy-Item (Join-Path $share "$port\copyright") (Join-Path $licenses "$port.txt")
}
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
foreach ($component in @(@('nsec.cryptography','26.4.0'), @('libsodium','1.0.22'))) {
    $package = Join-Path $nugetPackages "$($component[0])\$($component[1])"
    Copy-Item (Join-Path $package 'LICENSE') (Join-Path $licenses "$($component[0]).txt")
    if (Test-Path (Join-Path $package 'NOTICE')) { Copy-Item (Join-Path $package 'NOTICE') (Join-Path $licenses "$($component[0])-NOTICE.txt") }
    if (Test-Path (Join-Path $package 'AUTHORS')) { Copy-Item (Join-Path $package 'AUTHORS') (Join-Path $licenses "$($component[0])-AUTHORS.txt") }
}
[System.IO.File]::WriteAllText((Join-Path $licenses "OUTROS.txt"), @"
Também incluídos, sob as licenças dos respectivos autores:
- .NET Runtime e ASP.NET Core - MIT - https://github.com/dotnet/runtime
- Windows App SDK / WinUI 3 - Microsoft (redistribuível) - https://github.com/microsoft/WindowsAppSDK
- CommunityToolkit.Mvvm - MIT - https://github.com/CommunityToolkit/dotnet
- NSec.Cryptography 26.4.0 - MIT - https://github.com/ektrah/nsec
- libsodium 1.0.22 - ISC - https://github.com/jedisct1/libsodium
- C++/WinRT (headers do SDK do Windows) - MIT - https://github.com/microsoft/cppwinrt
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
