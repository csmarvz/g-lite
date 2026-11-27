param([string]$Out, [string]$Border = "3A3A3A")
# The demo window as Windows shows it: cropped to its visible frame (DWM's extended frame bounds, without the invisible resize
# borders PrintWindow includes), its corners rounded (8 px at 100%), a 1 px border and a soft shadow, on a transparent background.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Shot2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out RECT r, int size);
}
"@
$p = Get-Process GLite | Where-Object { $_.Path -like "*AppData*" } | Select-Object -First 1
$h = $p.MainWindowHandle
$window = New-Object Shot2+RECT; [Shot2]::GetWindowRect($h, [ref]$window) | Out-Null
$frame = New-Object Shot2+RECT; [Shot2]::DwmGetWindowAttribute($h, 9, [ref]$frame, 16) | Out-Null
$full = New-Object System.Drawing.Bitmap ($window.R - $window.L), ($window.B - $window.T)
$g = [System.Drawing.Graphics]::FromImage($full); $dc = $g.GetHdc(); [Shot2]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
$w = $frame.R - $frame.L; $ht = $frame.B - $frame.T
$crop = $full.Clone((New-Object System.Drawing.Rectangle ($frame.L - $window.L), ($frame.T - $window.T), $w, $ht), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$full.Dispose()

$scale = 1.5; $radius = 8 * $scale; $margin = [int](24 * $scale); $drop = [int](8 * $scale)
$result = New-Object System.Drawing.Bitmap ($w + 2 * $margin), ($ht + 2 * $margin + $drop), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($result)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.Clear([System.Drawing.Color]::Transparent)
function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$r) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
  $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
  $path.CloseFigure(); $path
}
# The shadow: rounded rectangles growing outwards, each faint, a little lower than the window.
$steps = $margin
for ($i = $steps; $i -ge 1; $i--) {
  $alpha = [int](70 * [Math]::Pow(1 - $i / $steps, 2) / 6) + 1
  $path = New-RoundedPath ($margin - $i) ($margin - $i + $drop) ($w + 2 * $i) ($ht + 2 * $i) ($radius + $i)
  $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha, 0, 0, 0))
  $g.FillPath($brush, $path); $brush.Dispose(); $path.Dispose()
}
# The window, clipped to its rounded frame.
$windowPath = New-RoundedPath $margin $margin $w $ht $radius
$texture = New-Object System.Drawing.TextureBrush $crop
$texture.TranslateTransform($margin, $margin)
$g.FillPath($texture, $windowPath)
$color = [System.Drawing.ColorTranslator]::FromHtml("#" + $Border)
$pen = New-Object System.Drawing.Pen $color, 1
$outline = New-RoundedPath ($margin + 0.5) ($margin + 0.5) ($w - 1) ($ht - 1) $radius
$g.DrawPath($pen, $outline)
$g.Dispose()
$result.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out ($($result.Width)x$($result.Height), window $w x $ht)"
$result.Dispose(); $crop.Dispose()
