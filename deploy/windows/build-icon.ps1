# Rasteriza o SVG original (subconjunto rect/path) com WPF e gera um ICO multirresolução.
# Sem ferramentas externas; executar no Windows com PowerShell 5.1 ou 7.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase
$assets = Join-Path $PSScriptRoot "..\..\src\Coconut.App\Assets"
[xml]$svg = Get-Content (Join-Path $assets "Coconut.svg") -Raw
$drawing = New-Object System.Windows.Media.DrawingGroup
$context = $drawing.Open()
try {
    foreach ($element in $svg.DocumentElement.ChildNodes) {
        if ($element.LocalName -notin @("rect", "path")) { continue }
        $brush = if ($element.GetAttribute("fill") -eq "none") { $null } else {
            [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($element.GetAttribute("fill"))
        }
        $pen = $null
        if ($element.HasAttribute("stroke")) {
            $stroke = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($element.GetAttribute("stroke"))
            $pen = [System.Windows.Media.Pen]::new($stroke, [double]$element.GetAttribute("stroke-width"))
            $pen.StartLineCap = $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
        }
        if ($element.LocalName -eq "rect") {
            $rect = [System.Windows.Rect]::new([double]$element.x, [double]$element.y, [double]$element.width, [double]$element.height)
            $context.DrawRoundedRectangle($brush, $pen, $rect, [double]$element.rx, [double]$element.rx)
        } else {
            $context.DrawGeometry($brush, $pen, [System.Windows.Media.Geometry]::Parse($element.d))
        }
    }
} finally { $context.Close() }
$images = @()
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
    $context.DrawDrawing($drawing)
    $context.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    $images += ,@($size, $stream.ToArray())
    if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assets "Coconut.png"), $stream.ToArray()) }
    $stream.Dispose()
}
$stream = [System.IO.File]::Create((Join-Path $assets "Coconut.ico"))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry[0] -eq 256) { 0 } else { $entry[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry[1].Length); $writer.Write([uint32]$offset)
        $offset += $entry[1].Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry[1]) }
} finally { $writer.Dispose() }
Write-Host "Ícone gerado em $assets"
