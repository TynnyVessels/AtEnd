$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $projectRoot '.tools\dotnet'
$toolStateRoot = Join-Path $projectRoot '.tools\state'
$godotExe = Join-Path $projectRoot '.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'

if (-not (Test-Path -LiteralPath $godotExe)) {
    throw "Godot executable not found at $godotExe"
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $toolStateRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $toolStateRoot 'nuget-packages'
$env:APPDATA = Join-Path $toolStateRoot 'AppData\Roaming'
$env:LOCALAPPDATA = Join-Path $toolStateRoot 'AppData\Local'

New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:APPDATA,$env:LOCALAPPDATA | Out-Null

Start-Process -FilePath $godotExe -ArgumentList '--path', $projectRoot -WorkingDirectory $projectRoot
