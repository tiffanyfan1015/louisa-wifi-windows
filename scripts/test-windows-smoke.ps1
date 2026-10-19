$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'artifacts\win-x64\LouisaWifi.exe'
$output = Join-Path $root 'artifacts\ui-smoke'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run build-windows.ps1 -Publish first' }
$process = Start-Process -FilePath $exe -ArgumentList @('--smoke-test', ('"{0}"' -f $output)) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) {
    $process.Kill()
    throw 'Published app smoke test timed out'
}
if ($process.ExitCode -ne 0) { throw "Published app smoke test exited with $($process.ExitCode)" }
Get-Content -LiteralPath (Join-Path $output 'smoke.txt') -Encoding UTF8
