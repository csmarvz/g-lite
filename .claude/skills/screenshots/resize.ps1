param([int]$Width, [int]$Height)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win { [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags); }
"@
$p = Get-Process GLite | Where-Object { $_.Path -like "*AppData*" } | Select-Object -First 1
[Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 100, 50, $Width, $Height, 0x0004) | Out-Null
"resized"
