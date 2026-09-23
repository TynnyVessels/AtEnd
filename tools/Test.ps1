$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $projectRoot '.tools\dotnet'
$toolStateRoot = Join-Path $projectRoot '.tools\state'
$testProject = Join-Path $projectRoot 'tests\AtEnd.Core.Tests\AtEnd.Core.Tests.csproj'

if (-not (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe'))) {
    throw ".NET SDK not found at $dotnetRoot"
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $toolStateRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $toolStateRoot 'nuget-packages'
$env:APPDATA = Join-Path $toolStateRoot 'AppData\Roaming'
$env:LOCALAPPDATA = Join-Path $toolStateRoot 'AppData\Local'

New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:APPDATA,$env:LOCALAPPDATA | Out-Null

& (Join-Path $dotnetRoot 'dotnet.exe') run --project $testProject --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
