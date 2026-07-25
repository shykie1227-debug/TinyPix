$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$rootPath = Join-Path $env:TEMP 'TinyPix-WinUI-Gate-Readonly'
$reportPath = Join-Path (Join-Path $env:TEMP 'TinyPix-WinUI-Gate') 'readonly-report.txt'
$process = Get-Process TinyPix.WinUIGate -ErrorAction Stop | Select-Object -First 1
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$statusCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '⛔ portable.flag 缺失或目录不可写')
$buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '开始本地验证任务')
$status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
$button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
$writeBlocked = $false
try {
    [IO.File]::WriteAllText((Join-Path $rootPath 'should-not-write.txt'), 'blocked')
} catch [UnauthorizedAccessException] {
    $writeBlocked = $true
}
$passed = $null -ne $status -and $null -ne $button -and -not $button.Current.IsEnabled -and $writeBlocked -and -not $process.HasExited
"$(Get-Date -Format o) READONLY statusVisible=$($null -ne $status) startEnabled=$($button.Current.IsEnabled) writeBlocked=$writeBlocked processAlive=$(-not $process.HasExited) passed=$passed" | Set-Content -LiteralPath $reportPath -Encoding utf8
if (-not $passed) { throw 'Read-only portable root was not safely blocked.' }
