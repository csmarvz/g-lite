param([int]$Dpi, [int]$FromDpi, [int]$Width = 0, [int]$Height = 0)
# WM_DPICHANGED to a normal (not maximized) window, as when it moves to a screen with another scale. lParam points to the suggested
# bounds in this process: Windows marshals the RECT for this message. The window keeps its top left, its size scaled or as given.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Dpi2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
$p = Get-Process GLite | Where-Object { $_.Path -like "*AppData*" } | Select-Object -First 1
$h = $p.MainWindowHandle
$r = New-Object Dpi2+RECT
[Dpi2]::GetWindowRect($h, [ref]$r) | Out-Null
$ratio = $Dpi / $FromDpi
$w = [int](($r.R - $r.L) * $ratio); $ht = [int](($r.B - $r.T) * $ratio)
if ($Width -gt 0) { $w = $Width; $ht = $Height }
$memory = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(16)
[System.Runtime.InteropServices.Marshal]::Copy([int[]]@($r.L, $r.T, ($r.L + $w), ($r.T + $ht)), 0, $memory, 4)
[Dpi2]::SendMessage($h, 0x02E0, [IntPtr](($Dpi -shl 16) -bor $Dpi), $memory) | Out-Null
[System.Runtime.InteropServices.Marshal]::FreeHGlobal($memory)
"sent $Dpi, window $w x $ht"
