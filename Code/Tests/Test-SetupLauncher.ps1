# Runs the real CMD -> Windows PowerShell entry point using current built bytes.
# An already-running tray must reject setup before discovery/install changes.
$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $project ('Data\IconVisibilityTests\Launcher-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $evidence 'Extracted'
New-Item -ItemType Directory -Path (Join-Path $package 'Data\Stage') -Force | Out-Null
if (@(Get-Process AirPodsConnect -ErrorAction SilentlyContinue).Count -ne 1) { throw 'Expected one existing live tray for the protected setup check.' }
$beforePid = (Get-Process AirPodsConnect).Id
foreach ($file in @('Setup.ps1','Setup.Core.ps1','SETUP.cmd','Remove.ps1','UNINSTALL.cmd','OPEN-SETUP-LOG.cmd','START HERE.txt')) {
    Copy-Item -LiteralPath (Join-Path $project ('Share\' + $file)) -Destination (Join-Path $package $file)
}
foreach ($file in @('Install.ps1','Uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $project $file) -Destination (Join-Path $package $file) }
Get-ChildItem -LiteralPath (Join-Path $project 'Data\Stage') -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination (Join-Path $package 'Data\Stage')
$manifest = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
    @{Path=$_.FullName.Substring($package.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
@{Product='AirPodsConnectShare';Architecture='win-x64';BundledRuntime=$false;Files=$manifest} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding UTF8
$oldModules = $env:PSModulePath; $oldTemp = $env:TEMP
try {
    # Deliberately inherit PowerShell7's module path through cmd; SETUP must fix
    # only its own environment for Windows PowerShell5.1's Get-FileHash.
    $env:PSModulePath = Join-Path $PSHOME 'Modules'; $env:TEMP = $evidence
    $command = '"' + (Join-Path $package 'SETUP.cmd') + '" -CheckOnly < NUL'
    $output = & "$env:SystemRoot\System32\cmd.exe" /d /c $command 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $evidence 'cmd-protected.log') -Encoding UTF8
    if ($code -ne 1 -or ($output -join ' ') -notmatch 'AirPods Connect is already running\.|An installation or previous setup already exists') { throw "Expected protected setup refusal, got exit $code : $output" }
    $transcript = Get-Content -LiteralPath (Join-Path $evidence 'AirPodsConnect-Setup.log') -Raw
    if ($transcript -notmatch 'already running|already exists' -or $transcript -notmatch 'PSVersion: 5\.1') { throw 'Windows PowerShell5.1 persistent setup log missing expected refusal.' }
    Copy-Item -LiteralPath (Join-Path $evidence 'AirPodsConnect-Setup.log') -Destination (Join-Path $evidence 'protected-transcript.log')
    $missing = Join-Path $evidence 'Incomplete'; New-Item -ItemType Directory -Path $missing | Out-Null
    Copy-Item -LiteralPath (Join-Path $package 'SETUP.cmd') -Destination (Join-Path $missing 'SETUP.cmd')
    $output = & "$env:SystemRoot\System32\cmd.exe" /d /c ('"' + (Join-Path $missing 'SETUP.cmd') + '" < NUL') 2>&1
    $missingCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $evidence 'cmd-incomplete.log') -Encoding UTF8
    if ($missingCode -ne 1 -or ($output -join ' ') -notmatch 'Extract All') { throw 'Incomplete extraction did not preserve expected CMD refusal.' }
    if ((Get-Content -LiteralPath (Join-Path $evidence 'AirPodsConnect-Setup.log') -Raw) -notmatch 'Extract All') { throw 'Incomplete extraction log missing.' }
} finally { $env:PSModulePath=$oldModules; $env:TEMP=$oldTemp }
if ((Get-Process AirPodsConnect).Id -ne $beforePid) { throw 'Live tray changed during protected setup checks.' }
[pscustomobject]@{Result='Passed';CmdProtectedExit=$code;CmdIncompleteExit=$missingCode;Checks=7;LiveTrayPid=$beforePid;Evidence=$evidence;ReleaseCreated=$false} | ConvertTo-Json
