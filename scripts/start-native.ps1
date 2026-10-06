param([string]$Url)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$widgetExecutable = Join-Path $projectDirectory 'release\native\FamicomPlayer.exe'
$singleFileExecutable = Join-Path $projectDirectory 'release\single-file\FamicomPlayer.exe'
if (Test-Path -LiteralPath $singleFileExecutable) { $widgetExecutable = $singleFileExecutable }
if (-not (Test-Path -LiteralPath $widgetExecutable)) {
    & (Join-Path $projectDirectory 'build.ps1')
}
if ($Url) {
    Start-Process -FilePath $widgetExecutable -ArgumentList $Url
} else {
    Start-Process -FilePath $widgetExecutable
}
