<#
.SYNOPSIS
  Gera a versão distribuível do LessonsPlayer: pasta self-contained (não exige .NET instalado) e um .zip.

.EXAMPLE
  ./scripts/publish.ps1
  ./scripts/publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/publish/LessonsPlayer-$Runtime"
$zip = "$output.zip"

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
if (Test-Path $zip) { Remove-Item -Force $zip }

# Sem PublishSingleFile: o Whisper.net carrega as bibliotecas nativas da pasta runtimes/.
dotnet publish (Join-Path $root 'src/LessonsPlayer.App/LessonsPlayer.App.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained `
    -p:PublishReadyToRun=true `
    --output $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)" }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal

$folderMb = (Get-ChildItem $output -Recurse | Measure-Object Length -Sum).Sum / 1MB
$zipMb = (Get-Item $zip).Length / 1MB
Write-Host ("Pasta: {0} ({1:N0} MB)" -f $output, $folderMb)
Write-Host ("Zip:   {0} ({1:N0} MB)" -f $zip, $zipMb)
