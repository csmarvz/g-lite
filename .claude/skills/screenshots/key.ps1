param([long]$Handle, [int]$Key, [int]$Times = 1)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KeyPost { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
for ($i = 0; $i -lt $Times; $i++) {
  [KeyPost]::PostMessage([IntPtr]$Handle, 0x0100, [IntPtr]$Key, [IntPtr]1) | Out-Null
  Start-Sleep -Milliseconds 50
  [KeyPost]::PostMessage([IntPtr]$Handle, 0x0101, [IntPtr]$Key, [IntPtr]([long]0xC0000001 -band 0xFFFFFFFF)) | Out-Null
  Start-Sleep -Milliseconds 300
}
"sent"
