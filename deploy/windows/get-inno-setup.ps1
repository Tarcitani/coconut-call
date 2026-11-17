# Compilador fixado por versão e SHA-256 publicado na release oficial do Inno Setup.
# Ferramenta de desenvolvimento: não é necessária no computador de quem instala Coconut.
$ErrorActionPreference = "Stop"
$version = "7.1.1"
$expectedHash = "8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0"
$toolsDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\artifacts\tools"))
$compiler = Join-Path $toolsDir "inno-$version\ISCC.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $download = Join-Path $toolsDir "innosetup-$version-x64.exe"
    if (-not (Test-Path -LiteralPath $download)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing "https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-$version-x64.exe" -OutFile $download
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "SHA-256 inválido no compilador Inno Setup. Não será executado: $download"
    }
    $destination = Split-Path $compiler
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', ('/DIR="{0}"' -f $destination)) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) {
        throw "Instalação do compilador falhou (código $($process.ExitCode))."
    }
}
$compiler
