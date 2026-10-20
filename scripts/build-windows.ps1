param([switch]$Publish, [switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$solution = Join-Path $root 'windows\LouisaWifi.sln'
if (-not $SkipRestore) {
    & $dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
}
& $dotnet build $solution -c Release --no-restore --disable-build-servers -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $dotnet run --project (Join-Path $root 'windows\LouisaWifi.Tests') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
if ($Publish) {
    $restoreFlags = @()
    if ($SkipRestore) { $restoreFlags += '--no-restore' }
    & $dotnet publish (Join-Path $root 'windows\LouisaWifi.App') -c Release -r win-x64 --self-contained true --disable-build-servers -m:1 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o (Join-Path $root 'artifacts\win-x64') @restoreFlags
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath (Join-Path $root 'docs\README-WINDOWS.md') -Destination (Join-Path $root 'artifacts\win-x64\README-WINDOWS.md')
    Copy-Item -LiteralPath (Join-Path $root 'docs\PORTING_NOTES.md') -Destination (Join-Path $root 'artifacts\win-x64\PORTING_NOTES.md')
}
