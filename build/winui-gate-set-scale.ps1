$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$target = (Get-Content -LiteralPath (Join-Path $gateRoot 'scale-target.txt') -Raw).Trim()
if ($target -notin @('100%', '125%', '150%')) { throw "Unsupported scale target: $target" }
Start-Process 'ms-settings:display'
Start-Sleep -Seconds 4
$root = [System.Windows.Automation.AutomationElement]::RootElement
$scaleIdCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox')
$scale = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $scaleIdCondition)
if ($null -eq $scale) { throw 'Windows display scaling control was not found.' }
$scale.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 500
$typeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$item = $null
$listItems = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $typeCondition)
foreach ($candidate in $listItems) {
    if ($candidate.Current.Name.StartsWith($target, [StringComparison]::Ordinal)) {
        $item = $candidate
        break
    }
}
if ($null -eq $item) { throw "Windows display scale option was not found: $target" }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 5
"$(Get-Date -Format o) SCALE selected=$target" | Set-Content -LiteralPath (Join-Path $gateRoot 'scale-change-report.txt') -Encoding utf8
