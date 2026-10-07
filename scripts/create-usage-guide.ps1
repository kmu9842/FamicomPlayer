param(
    [string]$CaptureDirectory = (Join-Path $PSScriptRoot '..\docs\screenshots'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\docs\FamicomPlayer-1.0.0-guide.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$guideWidth = 2000
$guideHeight = 2680
$visual = New-Object Windows.Media.DrawingVisual
$drawing = $visual.RenderOpen()
$ink = '#28323A'
$muted = '#65717A'
$accent = '#226D67'

function Brush([string]$color) { return [Windows.Media.BrushConverter]::new().ConvertFromString($color) }
function Box([double]$x, [double]$y, [double]$width, [double]$height, [string]$color, [double]$radius = 20) {
    $drawing.DrawRoundedRectangle((Brush $color), $null, [Windows.Rect]::new($x,$y,$width,$height), $radius,$radius)
}
function Text([double]$x, [double]$y, [string]$content, [double]$size = 28, [string]$color = $ink, [double]$width = 1800, [bool]$bold = $false) {
    $content = $content.Replace('`n', "`n")
    $weight = if ($bold) { [Windows.FontWeights]::Bold } else { [Windows.FontWeights]::Normal }
    $face = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Malgun Gothic'), [Windows.FontStyles]::Normal, $weight, [Windows.FontStretches]::Normal)
    $formatted = [Windows.Media.FormattedText]::new($content, [Globalization.CultureInfo]::GetCultureInfo('ko-KR'), [Windows.FlowDirection]::LeftToRight, $face, $size, (Brush $color), 1.0)
    $formatted.MaxTextWidth = $width
    $formatted.LineHeight = $size * 1.5
    $drawing.DrawText($formatted, [Windows.Point]::new($x,$y))
}
function Shot([string]$filename, [double]$x, [double]$y, [double]$width) {
    $path = [IO.Path]::GetFullPath((Join-Path $CaptureDirectory $filename))
    if (!(Test-Path -LiteralPath $path)) { throw "Missing capture: $filename" }
    $bitmap = [Windows.Media.Imaging.BitmapImage]::new([Uri]::new($path))
    $height = $bitmap.PixelHeight * $width / $bitmap.PixelWidth
    $drawing.DrawImage($bitmap, [Windows.Rect]::new($x,$y,$width,$height))
}
function Number([double]$x, [double]$y, [string]$number) {
    $drawing.DrawEllipse((Brush $accent), [Windows.Media.Pen]::new((Brush '#FFFFFF'), 3), [Windows.Point]::new($x,$y), 23,23)
    Text ($x-8) ($y-19) $number 25 '#FFFFFF' 40 $true
}
function Step([double]$x, [double]$y, [string]$number, [string]$title, [string]$detail) {
    Number ($x+24) ($y+25) $number
    Text ($x+64) $y $title 32 $ink 650 $true
    Text ($x+64) ($y+52) $detail 26 $muted 680
}

Box 0 0 $guideWidth $guideHeight '#F3F5F3' 0
Text 64 40 'FAMICOMPLAYER  /  1.0.0' 26 $accent 1500 $true
Text 60 84 '게임팩처럼 꽂고, TV처럼 재생하세요' 55 $ink 1800 $true
Text 64 170 '실제 앱 화면으로 보는 빠른 사용가이드' 27 $muted

Box 60 238 1880 740 '#FFFFFF'
Text 90 265 '01  재생과 기본 조작' 35 $ink 1000 $true
Box 85 330 1035 615 '#323D42'
Shot '01-player.png' 105 334 900
Number 520 925 '1'
Number 310 770 '2'
Number 895 570 '3'
Number 300 650 '4'
Step 1160 350 '1' '링크를 붙여 넣고 Enter' '아래 주소창에 YouTube 영상 또는 재생목록 링크를 넣습니다.'
Step 1160 484 '2' 'TV 화면 · 재생 버튼 · Space' '철컥 소리와 삽입 동작이 끝나면 재생됩니다. 다시 누르면 일시정지합니다.'
Step 1160 622 '3' '볼륨과 이동' 'TV 아래 다이얼 / 패드 ↑↓: 음량   ·   ←→: 10초 이동'
Step 1160 750 '4' '게임팩과 설정' '팩 또는 저장 버튼: 보관함   ·   톱니: 설정   ·   CC: 자막'
Text 1224 885 '위쪽 TV 다이얼: YouTube 브라우저 / 로그인' 25 $accent 660 $true

Text 64 1020 '02  게임팩 저장과 모음' 35 $ink 1700 $true
Text 64 1073 '게임팩 보관함' 28 $accent 580 $true
Text 704 1073 '겹쳐진 팩 = 모음' 28 $accent 580 $true
Text 1344 1073 '모음 안의 게임팩' 28 $accent 580 $true
Shot '02-library.png' 60 1130 600
Shot '03-collections.png' 700 1130 600
Shot '04-collection-contents.png' 1340 1130 600
Text 65 1640 '「+ 게임팩 / 모음 추가」 → 링크 입력`n「저장」 또는 「넣고 재생」' 26 $ink 590
Text 705 1640 '재생목록 링크 → 「모음 저장」`n모음을 두 번 누르면 안으로 이동' 26 $ink 590
Text 1345 1640 '팩 선택 → 「넣고 재생」`n「←」로 전체 보관함에 돌아가기' 26 $ink 590

Text 64 1780 '03  설정과 보관함 관리' 35 $ink 1700 $true
Shot '05-settings-top.png' 60 1850 365
Shot '06-settings-bottom.png' 465 1850 365
Text 880 1845 '크기 · 화면 · 소리' 33 $accent 1000 $true
Text 880 1900 '• 전체 크기: 50–300%, 「기본 크기」로 복원`n• Ctrl + 휠 또는 오른쪽 아래 모서리로도 크기 조절`n• 브라운관 필터, 깜빡임, 삽입·부는 소리를 설정`n• 아래로 스크롤하면 소리 출력 장치와 OBS 연결`n• 「유튜브 페이지 보기」에서 로그인 후 위젯으로 복귀' 27 $ink 990
Text 880 2150 '모음 풀기와 삭제' 33 $accent 1000 $true
Text 880 2205 '「모음 풀기」는 게임팩을 유지합니다.`n「삭제」는 확인 후 모음 안의 게임팩까지 지웁니다.' 27 $ink 990
Shot '08-delete-confirmation.png' 1110 2310 590
Text 64 2614 'Windows 10/11 x64 · WebView2 Runtime 필요     |     화면의 영상과 보관함은 사용 예시입니다.' 24 $muted 1770
Text 1800 2614 'GreenGarnets' 19 $muted 170

$drawing.Close()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($guideWidth,$guideHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$stream = [IO.File]::Create($resolvedOutput)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
Get-Item -LiteralPath $resolvedOutput | Select-Object FullName,Length
