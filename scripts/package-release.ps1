param([string]$Executable = (Join-Path $PSScriptRoot '..\release\single-file\FamicomPlayer.exe'))
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $projectDirectory 'native\FamicomPlayer.csproj'))).Project.PropertyGroup.Version
$releaseDirectory = Join-Path $projectDirectory "release\$version"
$bundleDirectory = Join-Path $releaseDirectory 'package'
New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
$versionInfo = (Get-Item -LiteralPath $Executable).VersionInfo
if ($versionInfo.FileVersion -ne "$version.0") { throw 'Executable version does not match the project.' }
Copy-Item -LiteralPath $Executable -Destination (Join-Path $releaseDirectory 'FamicomPlayer.exe') -Force
Copy-Item -LiteralPath $Executable -Destination (Join-Path $bundleDirectory 'FamicomPlayer.exe') -Force
$guideName = "FamicomPlayer-$version-guide.png"
Copy-Item -LiteralPath (Join-Path $projectDirectory "docs\$guideName") -Destination (Join-Path $releaseDirectory $guideName) -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory "docs\$guideName") -Destination (Join-Path $bundleDirectory $guideName) -Force
$instructions = @"
FamicomPlayer $version

1. FamicomPlayer.exe를 실행합니다.
2. 아래 주소창에 YouTube 영상 또는 재생목록 링크를 붙여 넣고 Enter를 누릅니다.
3. 함께 들어 있는 $guideName에서 각 화면의 사용법을 확인하세요.

Windows 10 2004 이상 / Windows 11 x64와 Microsoft Edge WebView2 Runtime이 필요합니다.
WebView2: https://developer.microsoft.com/microsoft-edge/webview2
.NET 런타임과 이미지 리소스는 실행 파일에 포함되어 있습니다.

설정과 보관함: %LOCALAPPDATA%\FamicomPlayerNative
업데이트: 앱 종료 후 EXE 교체. 기존 설정과 보관함은 유지됩니다.
저장소: https://github.com/kmu9842/FamicomPlayer
"@
[IO.File]::WriteAllText((Join-Path $bundleDirectory 'README.txt'), $instructions, [Text.UTF8Encoding]::new($true))
$zipPath = Join-Path $releaseDirectory "FamicomPlayer-$version-win-x64.zip"
$bundleFiles = @('FamicomPlayer.exe', $guideName, 'README.txt') | ForEach-Object { Join-Path $bundleDirectory $_ }
Compress-Archive -LiteralPath $bundleFiles -DestinationPath $zipPath -Force
$assetNames = @('FamicomPlayer.exe', $guideName, [IO.Path]::GetFileName($zipPath))
$checksums = foreach ($name in $assetNames) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $releaseDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $releaseDirectory -File | Select-Object Name,Length
