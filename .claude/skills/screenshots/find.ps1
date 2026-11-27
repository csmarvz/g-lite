param([string]$Name, [string]$Type = "")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$proc = Get-Process GLite | Where-Object { $_.Path -like "*AppData*" } | Select-Object -First 1
$cond = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)), (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)))
$all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
foreach ($e in $all) { if ($Type -eq "" -or $e.Current.ControlType.ProgrammaticName -eq "ControlType.$Type") { $e.Current.NativeWindowHandle; break } }
