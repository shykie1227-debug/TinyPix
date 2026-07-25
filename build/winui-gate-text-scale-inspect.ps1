$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$report = Join-Path (Join-Path $env:TEMP 'TinyPix-WinUI-Gate') 'text-scale-inspect-report.txt'
Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue
Start-Process 'ms-settings:easeofaccess-display'
Start-Sleep -Seconds 5
$root = [System.Windows.Automation.AutomationElement]::RootElement
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($window in $windows) {
    if ($window.Current.Name -match '设置|Settings') {
        "WINDOW name=$($window.Current.Name) class=$($window.Current.ClassName)" | Add-Content $report -Encoding utf8
        $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $elements) {
            $type = $element.Current.ControlType
            if ($type -in @(
                [System.Windows.Automation.ControlType]::Slider,
                [System.Windows.Automation.ControlType]::Text,
                [System.Windows.Automation.ControlType]::Button)) {
                $range = ''
                $rangePattern = $null
                if ($element.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rangePattern)) {
                    $range = " value=$($rangePattern.Current.Value) min=$($rangePattern.Current.Minimum) max=$($rangePattern.Current.Maximum)"
                }
                "ELEMENT type=$($type.ProgrammaticName) name=$($element.Current.Name) id=$($element.Current.AutomationId) class=$($element.Current.ClassName)$range" | Add-Content $report -Encoding utf8
            }
        }
    }
}
