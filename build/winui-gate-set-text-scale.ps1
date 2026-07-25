$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$target = [double](Get-Content -LiteralPath (Join-Path $gateRoot 'text-scale-target.txt') -Raw).Trim()
if ($target -notin @(100, 200)) { throw "Unsupported text-scale target: $target" }

Start-Process 'ms-settings:easeofaccess-display'
Start-Sleep -Seconds 5
$root = [System.Windows.Automation.AutomationElement]::RootElement
$sliderCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_Slider')
$slider = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
if ($null -eq $slider) { throw 'Windows text-scale slider was not found.' }
$range = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
$range.SetValue($target)

$applyCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'SystemSettings_EaseOfAccess_Experience_TextScalingDesktop_ButtonRemove')
$apply = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $applyCondition)
if ($null -eq $apply) { throw 'Windows text-scale Apply button was not found.' }
$apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 8

$slider = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
$actual = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
if ([math]::Abs($actual - $target) -gt 0.1) { throw "Text scale expected $target but Windows reports $actual." }
"$(Get-Date -Format o) TEXT_SCALE selected=$target actual=$actual" |
    Set-Content -LiteralPath (Join-Path $gateRoot 'text-scale-change-report.txt') -Encoding utf8
[System.Windows.Forms.SendKeys]::SendWait('%{F4}')
