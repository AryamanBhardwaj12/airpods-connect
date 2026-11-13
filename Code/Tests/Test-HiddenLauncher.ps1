$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $project 'Code\Tests\bin\Release\net8.0-windows\IconVisibility.Tests.exe'
$result = Join-Path $project ('Data\IconVisibilityTests\hidden-launch-' + [guid]::NewGuid().ToString('N') + '.json')
$startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0}
$launch = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine='"' + $exe + '" --hidden-page-probe "' + $result + '"'; CurrentDirectory=(Split-Path $exe -Parent); ProcessStartupInformation=$startup
}
if ($launch.ReturnValue -ne 0) { throw "Probe launch failed: $($launch.ReturnValue)" }
try { $process = Get-Process -Id $launch.ProcessId -ErrorAction Stop } catch { $process = $null }
if ($process -and -not $process.WaitForExit(10000)) { throw "Synthetic page probe did not finish: $($launch.ProcessId)" }
$probe = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
if (-not $probe.NativeVisible -or $probe.Writes -ne 0 -or $probe.Runtime -notmatch '^8\.') { throw 'Hidden-launch page did not become visible safely.' }
[pscustomobject]@{Result='Passed';NativeVisible=$probe.NativeVisible;Writes=$probe.Writes;Runtime=$probe.Runtime;Evidence=$result;BluetoothRequests='None'} | ConvertTo-Json
