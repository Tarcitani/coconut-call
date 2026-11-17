# Gera o zip portátil e o instalador offline a partir do MESMO payload validado.
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = (Get-Content (Join-Path $PSScriptRoot "..\VERSION") -Raw).Trim(),
    [ValidatePattern('^(wss://[^/\s]+/ws)?$')]
    [string]$Server = "",
    [string]$InnoCompilerPath = "",
    [switch]$SignUpdate
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ($Server -and $SignUpdate) { throw "Pacotes particulares não entram no feed público de atualização." }
if (-not $InnoCompilerPath) { $InnoCompilerPath = & (Join-Path $PSScriptRoot "windows\get-inno-setup.ps1") }
if (-not (Test-Path -LiteralPath $InnoCompilerPath)) { throw "Compilador não encontrado: $InnoCompilerPath" }
& (Join-Path $PSScriptRoot "publish-app.ps1") -Version $Version -Server $Server
$name = if ($Server) { "Coconut-$Version-win-x64-servidor" } else { "Coconut-$Version-win-x64" }
$output = Join-Path $repo "artifacts\app"
$stage = Join-Path $output $name
& $InnoCompilerPath "/DAppVersion=$Version" "/DPayloadDir=$stage" "/DOutputDir=$output" "/DPackageName=$name" (Join-Path $PSScriptRoot "windows\Coconut.iss")
if ($LASTEXITCODE -ne 0) { throw "Compilação do instalador falhou" }
$setup = Join-Path $output "$name-Setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Instalador ausente: $setup" }
$files = @($setup, (Join-Path $output "$name.zip"))
if ($SignUpdate) {
    $manifest = Join-Path $output "coconut-update.json"
    dotnet run --project (Join-Path $repo 'tools\Coconut.Release') -c Release -- sign $Version $setup $manifest (Join-Path $repo 'src\Coconut.Updates\release-public-key.txt')
    if ($LASTEXITCODE -ne 0) { throw "Assinatura do manifesto de atualização falhou." }
    $files += $manifest
}
$checksums = foreach ($file in $files) {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}
$checksumPath = Join-Path $output "$name-SHA256SUMS.txt"
[System.IO.File]::WriteAllLines($checksumPath, [string[]]$checksums, [System.Text.UTF8Encoding]::new($false))
Write-Host "Instalador: $setup"
Write-Host "Verificação: $checksumPath"
