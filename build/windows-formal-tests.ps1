$ErrorActionPreference = 'Stop'

$sourceRoot = '\\Mac\Home\TinyPix\3.5pro'
$guestRoot = Join-Path $env:TEMP 'TinyPix-Formal-Tests'
$reportPath = Join-Path $guestRoot 'windows-formal-tests-report.txt'
$artifactPath = Join-Path $sourceRoot 'artifacts\windows\winui-feasibility-gate\windows-formal-tests-report-final.txt'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The locked .NET SDK is unavailable in the Windows guest.' }
Remove-Item -LiteralPath $guestRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $guestRoot -Force | Out-Null

function Record([string]$message) {
    Add-Content -LiteralPath $reportPath -Value "$(Get-Date -Format o) $message" -Encoding utf8
}

function Copy-Tree([string]$relativePath) {
    $source = Join-Path $sourceRoot $relativePath
    $destination = Join-Path $guestRoot $relativePath
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & robocopy $source $destination /E /XD bin obj | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed for $relativePath with $LASTEXITCODE." }
}

foreach ($file in @('global.json', 'Directory.Build.props', 'Directory.Packages.props', 'TinyPix.sln')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination (Join-Path $guestRoot $file) -Force
}
foreach ($directory in @(
    'src\TinyPix.Core',
    'src\TinyPix.Media',
    'src\TinyPix.Infrastructure',
    'src\TinyPix.App',
    'tests\TinyPix.Core.Tests',
    'tests\TinyPix.Media.Tests',
    'tests\TinyPix.Infrastructure.Tests'
)) {
    Copy-Tree $directory
}
Record 'COPY guestLocal=True exclusions=/XD bin obj'

$testProjects = @(
    'tests\TinyPix.Core.Tests\TinyPix.Core.Tests.csproj',
    'tests\TinyPix.Media.Tests\TinyPix.Media.Tests.csproj',
    'tests\TinyPix.Infrastructure.Tests\TinyPix.Infrastructure.Tests.csproj'
)
foreach ($project in $testProjects) {
    & $dotnet test (Join-Path $guestRoot $project) --configuration Release -p:RestoreLockedMode=true --nologo
    if ($LASTEXITCODE -ne 0) { throw "Windows test failed: $project" }
    Record "TEST project=$project result=0"
}

& $dotnet restore (Join-Path $guestRoot 'src\TinyPix.App\TinyPix.App.csproj') --locked-mode -p:EnableWindowsTargeting=true
if ($LASTEXITCODE -ne 0) { throw 'TinyPix.App locked restore failed.' }
Record 'RESTORE project=TinyPix.App.csproj locked=True result=0'
Record 'WINDOWS_FORMAL_TESTS passed=True'
Copy-Item -LiteralPath $reportPath -Destination $artifactPath -Force
