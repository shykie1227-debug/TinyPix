$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$projectPath = Join-Path $gateRoot 'TinyPix.WinUIGate.csproj'

[xml]$project = Get-Content -LiteralPath $projectPath
$propertyGroup = @($project.Project.PropertyGroup)[0]

function Set-ProjectProperty([string]$name, [string]$value) {
    $node = $propertyGroup.SelectSingleNode($name)
    if ($null -eq $node) {
        $node = $project.CreateElement($name)
        [void]$propertyGroup.AppendChild($node)
    }
    $node.InnerText = $value
}

Set-ProjectProperty 'TargetFramework' 'net10.0-windows10.0.19041.0'
Set-ProjectProperty 'TargetPlatformMinVersion' '10.0.19041.0'
Set-ProjectProperty 'Platforms' 'x64'
Set-ProjectProperty 'PlatformTarget' 'x64'
Set-ProjectProperty 'RuntimeIdentifier' 'win-x64'
Set-ProjectProperty 'WindowsPackageType' 'None'
Set-ProjectProperty 'WindowsAppSDKSelfContained' 'true'
Set-ProjectProperty 'WindowsAppSdkBootstrapInitialize' 'false'
Set-ProjectProperty 'WindowsAppSdkUndockedRegFreeWinRTInitialize' 'true'
Set-ProjectProperty 'SelfContained' 'true'
Set-ProjectProperty 'PublishSingleFile' 'false'
Set-ProjectProperty 'EnableMsixTooling' 'false'
Set-ProjectProperty 'EnableWinAppRunSupport' 'false'

$itemGroup = $project.CreateElement('ItemGroup')
foreach ($package in @(
    @{ Name = 'Microsoft.WindowsAppSDK.WinUI'; Version = '2.2.1' },
    @{ Name = 'Microsoft.WindowsAppSDK.Runtime'; Version = '2.2.0' }
)) {
    $reference = $project.CreateElement('PackageReference')
    $reference.SetAttribute('Include', $package.Name)
    $reference.SetAttribute('Version', $package.Version)
    [void]$itemGroup.AppendChild($reference)
}
[void]$project.Project.AppendChild($itemGroup)

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($projectPath, $settings)
$project.Save($writer)
$writer.Close()

Get-Content -LiteralPath $projectPath
dotnet restore $projectPath -r win-x64 --force-evaluate
dotnet build $projectPath -c Debug -r win-x64 --no-restore
