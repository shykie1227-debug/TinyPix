$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$report = Join-Path (Join-Path $env:TEMP 'TinyPix-WinUI-Gate') 'display-settings-report.txt'
Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
Start-Process 'ms-settings:display'
Start-Sleep -Seconds 4
$root = [System.Windows.Automation.AutomationElement]::RootElement
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($window in $windows) {
    if ($window.Current.Name -match '设置|Settings') {
        "WINDOW name=$($window.Current.Name) class=$($window.Current.ClassName)" | Add-Content $report -Encoding utf8
        $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $elements) {
            if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox -or $element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                "ELEMENT type=$($element.Current.ControlType.ProgrammaticName) name=$($element.Current.Name) id=$($element.Current.AutomationId) class=$($element.Current.ClassName)" | Add-Content $report -Encoding utf8
            }
        }
    }
}
$scaleIdCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox')
$scale = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $scaleIdCondition)
if ($null -ne $scale) {
    $scale.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $items) {
        if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            "SCALE_ITEM name=$($item.Current.Name) id=$($item.Current.AutomationId)" | Add-Content $report -Encoding utf8
        }
    }
}
