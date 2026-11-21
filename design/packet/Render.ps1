$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
function Read-View($name) {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $name))
    [Windows.Markup.XamlReader]::Parse($text)
}
function Snapshot($view, $w, $h, $file) {
    $view.Measure([Windows.Size]::new($w, $h))
    $view.Arrange([Windows.Rect]::new(0, 0, $w, $h))
    $view.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($view)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($file)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
$dashboard = Read-View 'DashboardStudy.xaml'
$scene = Read-View 'PacketScene.xaml'
$dashboard.FindName('SceneHost').Content = $scene
$animation = $scene.Resources['Dock']
$animation.Begin($scene, $true)
$animation.Pause($scene)
$animation.SeekAlignedToLastTick($scene,[TimeSpan]::FromMilliseconds(600),[Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
Snapshot $dashboard 1100 684 (Join-Path $PSScriptRoot 'Dashboard.png')
$scene.Background = $dashboard.Children[0].Background
$frames = [IO.Directory]::CreateTempSubdirectory('pd-frames-')
try {
    for ($i=0; $i -lt 36; $i++) {
        $animation.SeekAlignedToLastTick($scene,[TimeSpan]::FromMilliseconds(50*$i),[Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
        Snapshot $scene 384 260 (Join-Path $frames.FullName ('{0:D2}.png' -f $i))
    }
    python -c "from PIL import Image; import sys,pathlib; f=[Image.open(p).convert('RGB') for p in sorted(pathlib.Path(sys.argv[1]).glob('*.png'))]; f[0].save(sys.argv[2],save_all=True,append_images=f[1:],duration=50,loop=0)" $frames.FullName (Join-Path $PSScriptRoot 'Packet.gif')
    if ($LASTEXITCODE -ne 0) { throw 'GIF export failed.' }
} finally {
    foreach ($file in $frames.GetFiles('*.png')) { $file.Delete() }
    $frames.Delete()
}
