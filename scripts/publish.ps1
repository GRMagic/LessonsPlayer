<#
.SYNOPSIS
  Gera a versão distribuível do LessonsPlayer: um único .exe self-contained (não exige .NET instalado), dentro de uma pasta, e um .zip.

.EXAMPLE
  ./scripts/publish.ps1
  ./scripts/publish.ps1 -Runtime win-arm64
  ./scripts/publish.ps1 -Version 0.1.42   # versão do .exe (o CI usa o número da execução)
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/publish/LessonsPlayer-$Runtime"
$zip = "$output.zip"

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
if (Test-Path $zip) { Remove-Item -Force $zip }

# [string[]]: um "if" devolve a string solta, e o splat de uma string passaria um argumento por caractere (pwsh 7).
# Uma lista vazia some na chamada; um $null viraria um argumento vazio para o dotnet.
[string[]] $versionArgs = if ($Version) { "-p:Version=$Version" } else { @() }

# Arquivo único: o .exe leva dentro as DLLs gerenciadas, as nativas do WPF e do Whisper.net (runtimes/) e o modelo
# do VAD (IncludeAllContentForSelfExtract). Na primeira execução o .NET extrai tudo para %TEMP%\.net\LessonsPlayer
# (~200 MB, uns segundos; depois só reaproveita), e é de lá que o Whisper.net carrega as bibliotecas nativas.
# Sem DebugType=none o .pdb sairia ao lado do .exe; os runtimes de outras plataformas e os .xml saem em
# PruneUnusedPublishFiles (LessonsPlayer.App.csproj).
dotnet publish (Join-Path $root 'src/LessonsPlayer.App/LessonsPlayer.App.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    @versionArgs `
    --output $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)" }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal

$folderMb = (Get-ChildItem $output -Recurse | Measure-Object Length -Sum).Sum / 1MB
$zipMb = (Get-Item $zip).Length / 1MB
Write-Host ("Pasta: {0} ({1:N0} MB)" -f $output, $folderMb)
Write-Host ("Zip:   {0} ({1:N0} MB)" -f $zip, $zipMb)
