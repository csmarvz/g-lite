param([long]$Handle)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
[K]::PostMessage([IntPtr]$Handle, 0x0100, [IntPtr]0x20, [IntPtr]0x00390001) | Out-Null
Start-Sleep -Milliseconds 60
[K]::PostMessage([IntPtr]$Handle, 0x0101, [IntPtr]0x20, [IntPtr]([long]0xC0390001 -band 0xFFFFFFFF)) | Out-Null
"pressed"
