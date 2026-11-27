param([string]$Out = (Join-Path $env:TEMP "glite-shots"))
# The site's and README's screenshots: the four-device demo, the window at its narrowest and nearly square, at 150%, composed by
# compose.ps1 (rounded corners, border, shadow, transparent background), into $Out.
$s = $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
Get-Process GLite -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*AppData*" } | Stop-Process -Force
Start-Sleep 1
Start-Process "$env:LOCALAPPDATA\GLite\GLite.exe" -ArgumentList "--demo", "--few"
Start-Sleep 6
& "$s\resize.ps1" 800 800 | Out-Null
Start-Sleep 1
& "$s\dpi.ps1" -Dpi 144 -FromDpi 96 -Width 1326 -Height 1200 | Out-Null
Start-Sleep 2
Add-Type -AssemblyName UIAutomationClient
function Select-Item($name) { & "$s\press.ps1" (& "$s\find.ps1" $name "Button") | Out-Null; Start-Sleep 2 }
function Save-Shot($file, $border = "3A3A3A") { & "$s\compose.ps1" -Out "$Out\$file" -Border $border }
function Pick-Effect($effect) {
  & "$s\press.ps1" (& "$s\find.ps1" "Effect" "ComboBox") | Out-Null
  Start-Sleep 1
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $effect)
  $item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $pattern = $null
  if ($item -and $item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() } else { "couldn't pick $effect" }
  Start-Sleep 3
}
Select-Item "G515 TKL"; Pick-Effect "Aurora"; Start-Sleep 2; Save-Shot "keyboard-live.png"
Pick-Effect "Wave"; Save-Shot "keyboard.png"
Select-Item "G Pro Wireless"; Save-Shot "mouse.png"
Select-Item "Litra Beam LX"; Save-Shot "light.png"
Select-Item "Settings"; Save-Shot "settings.png"
$theme = & "$s\find.ps1" "Theme"
& "$s\key.ps1" $theme 0x27 | Out-Null
Start-Sleep 2
Select-Item "G515 TKL"; Save-Shot "keyboard-light.png" "D4D4D4"
Select-Item "Settings"
& "$s\key.ps1" (& "$s\find.ps1" "Theme") 0x25 | Out-Null
Start-Sleep 1
Get-Process GLite -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*AppData*" } | Stop-Process -Force
