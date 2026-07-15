param(
  [Parameter(Mandatory = $true)][string]$Path,
  [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$windowCondition = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Window
)
$dialog = $null
$lastWindowNames = @()
for ($i = 0; $i -lt ($TimeoutSeconds * 4) -and $null -eq $dialog; $i++) {
  $windows = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition)
  $lastWindowNames = @($windows | ForEach-Object { $_.Current.Name })
  foreach ($window in $windows) {
    if ($window.Current.Name -match 'Open|打开|Select|选择|Videos|Images') {
      $dialog = $window
      break
    }
  }
  if ($null -eq $dialog) { Start-Sleep -Milliseconds 250 }
}
if ($null -eq $dialog) { throw "Native file dialog did not become visible. Windows: $($lastWindowNames -join ' | ')" }

$editCondition = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Edit
)
$edits = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
$fileNameEdit = $null
foreach ($edit in $edits) {
  if ($edit.Current.AutomationId -eq '1148') { $fileNameEdit = $edit; break }
}
if ($null -eq $fileNameEdit -and $edits.Count -gt 0) { $fileNameEdit = $edits.Item($edits.Count - 1) }
if ($null -eq $fileNameEdit) { throw 'File name input was not found.' }
$valuePattern = $fileNameEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$valuePattern.SetValue($Path)

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
  [System.Windows.Automation.ControlType]::Button
)
$buttons = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
$openButton = $null
foreach ($button in $buttons) {
  if ($button.Current.Name -in @('Open', '打开', '&Open', '打开(&O)')) { $openButton = $button; break }
}
if ($null -eq $openButton) { throw 'Open button was not found.' }
$invokePattern = $openButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invokePattern.Invoke()
$Path
