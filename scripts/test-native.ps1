$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectDirectory 'build.ps1') -Test
$env:FAMICOMPLAYER_ARTIFACTS = Join-Path $projectDirectory 'artifacts\native'
New-Item -ItemType Directory -Path $env:FAMICOMPLAYER_ARTIFACTS -Force | Out-Null
$testProcess = Start-Process -FilePath (Join-Path $projectDirectory 'release\native\FamicomPlayer.exe') -ArgumentList '--smoke' -PassThru -WindowStyle Hidden
if (-not $testProcess.WaitForExit(180000)) {
    Stop-Process -Id $testProcess.Id
    throw 'Native desktop verification timed out.'
}
if ($testProcess.ExitCode -ne 0) {
    $failurePath = Join-Path $env:FAMICOMPLAYER_ARTIFACTS 'failure.json'
    if (Test-Path -LiteralPath $failurePath) { Get-Content -LiteralPath $failurePath -Encoding UTF8 }
    throw "Native desktop verification failed: $($testProcess.ExitCode)"
}
Get-Content -LiteralPath (Join-Path $env:FAMICOMPLAYER_ARTIFACTS 'verification.json') -Encoding UTF8
