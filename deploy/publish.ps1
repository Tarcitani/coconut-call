# Gera o pacote do servidor do Coconut para uma VM Linux.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publish.ps1 -Runtime linux-arm64
#      (linux-arm64 para VMs Ampere/ARM; linux-x64 para VMs AMD/Intel)
param(
    [ValidateSet("linux-x64", "linux-arm64")]
    [string]$Runtime = "linux-arm64"
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$stage = Join-Path $repo "artifacts\deploy\coconut-signaling-$Runtime"
$archive = Join-Path $repo "artifacts\deploy\coconut-signaling-$Runtime.tar.gz"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "server") | Out-Null

# Autocontido: a VM não precisa de .NET instalado.
dotnet publish (Join-Path $repo "src\Coconut.Signaling.Server") -c Release -r $Runtime --self-contained true `
    -p:InvariantGlobalization=true -p:DebugType=None -o (Join-Path $stage "server")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

foreach ($file in "install.sh", "Caddyfile", "coconut-signaling.service") {
    $source = Join-Path $repo "deploy\linux\$file"
    if ([System.IO.File]::ReadAllText($source).Contains("`r")) {
        throw "$file tem CRLF; scripts Linux precisam de LF (veja .gitattributes)."
    }
    Copy-Item $source $stage
}

# O tar do Windows não grava permissão de execução: o guia chama "sudo bash install.sh",
# e o próprio install.sh aplica chmod no binário do servidor.
if (Test-Path $archive) { Remove-Item $archive -Force }
tar -czf $archive -C (Split-Path $stage) (Split-Path $stage -Leaf)
if ($LASTEXITCODE -ne 0) { throw "tar falhou" }

$size = [Math]::Round((Get-Item $archive).Length / 1MB, 1)
"Pacote: $archive ($size MB)"
