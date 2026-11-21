$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$scene = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NetworkFlow.xaml')))
$scene.Measure([Windows.Size]::new(1000,506))
$scene.Arrange([Windows.Rect]::new(0,0,1000,506))
$animation = $scene.Resources['Flow']
$animation.Begin($scene,$true)
$animation.Pause($scene)
$folder = [IO.Directory]::CreateTempSubdirectory('network-flow-')
try {
    for ($i=0; $i -lt 90; $i++) {
        $animation.SeekAlignedToLastTick($scene,[TimeSpan]::FromMilliseconds(100*$i),[Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
        $scene.UpdateLayout()
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1000,506,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($scene)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path = Join-Path $folder.FullName ('{0:D2}.png' -f $i)
        $stream = [IO.File]::Create($path)
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        if ($i -eq 27) { Copy-Item -LiteralPath $path -Destination (Join-Path $PSScriptRoot 'Downloading.png') -Force }
    }
    python -c "from PIL import Image; import sys,pathlib; frames=[Image.open(p).convert('RGB') for p in sorted(pathlib.Path(sys.argv[1]).glob('*.png'))]; frames[0].save(sys.argv[2],save_all=True,append_images=frames[1:],duration=100,loop=0)" $folder.FullName (Join-Path $PSScriptRoot 'NetworkFlow.gif')
    if ($LASTEXITCODE -ne 0) { throw 'Animation export failed.' }
} finally {
    foreach ($file in $folder.GetFiles('*.png')) { $file.Delete() }
    $folder.Delete()
}
