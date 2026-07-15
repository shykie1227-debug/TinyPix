param(
  [string]$ExePath = "$env:USERPROFILE\Desktop\tiny\TinyPix-Pro-3.5.1-Windows-x64-Portable.exe",
  [string]$OutputPath = "C:\Mac\Home\TinyPix\3.5pro\artifacts\windows\uia-controls.txt"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Get-Process '*TinyPix*' -ErrorAction SilentlyContinue | Stop-Process -Force
$launcher = Start-Process $ExePath -PassThru
$process = $null
for ($i = 0; $i -lt 80 -and $null -eq $process; $i++) {
  Start-Sleep -Milliseconds 250
  $process = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq 'TinyPix Pro'
  } | Select-Object -First 1
}
if ($null -eq $process) { throw 'TinyPix window did not become ready.' }
Start-Sleep -Seconds 2
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$all = $root.FindAll(
  [System.Windows.Automation.TreeScope]::Descendants,
  [System.Windows.Automation.Condition]::TrueCondition
)
$lines = foreach ($element in $all) {
  $name = $element.Current.Name
  $type = $element.Current.ControlType.ProgrammaticName
  $id = $element.Current.AutomationId
  if ($name -or $id) { "$type`t$id`t$name" }
}
$lines | Set-Content $OutputPath -Encoding utf8
$process.CloseMainWindow() | Out-Null
if (!$process.WaitForExit(5000)) { $process.Kill() }
$OutputPath
