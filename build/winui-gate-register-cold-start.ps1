$ErrorActionPreference = 'Stop'

$taskName = 'TinyPixWinUIGateColdStart'
$wrapperPath = '\\Mac\Home\TinyPix\3.5pro\build\winui-gate-cold-start-wrapper.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$wrapperPath`""
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Write-Output "REGISTERED_TASK=$taskName USER=$($principal.UserId) RUN_LEVEL=$($principal.RunLevel)"
