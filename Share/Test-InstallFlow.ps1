# Run the actual installer against a temporary app directory. Only read-only
# hardware queries are real; shortcut COM and tray launches are intercepted.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Core.ps1')
$project = Split-Path $PSScriptRoot -Parent
$sandbox = Join-Path $project ('Data\InstallFlowTest\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $sandbox 'Data\Stage') -Force | Out-Null
Copy-Item -Path (Join-Path $project 'Data\Stage\*') -Destination (Join-Path $sandbox 'Data\Stage')
Copy-Item -LiteralPath (Join-Path $project 'Install.ps1') -Destination (Join-Path $sandbox 'Install.ps1')
$original = Get-Content -LiteralPath (Join-Path $project 'Data\settings.json') -Raw | ConvertFrom-Json
$global:AirPodsInstallTest = [pscustomobject]@{ExpectedExe=(Join-Path $sandbox 'App\AirPodsConnect.exe');SavedLinks=@();Launches=@()}

function New-Object {
    [CmdletBinding()]
    param([string]$ComObject)
    if ($ComObject -ne 'WScript.Shell') { throw 'Unexpected object creation in installer test.' }
    $shell = [pscustomobject]@{}
    $shell | Add-Member -MemberType ScriptMethod -Name CreateShortcut -Value {
        param($path)
        $link = [pscustomobject]@{Path=$path;TargetPath=$global:AirPodsInstallTest.ExpectedExe;WorkingDirectory='';IconLocation='';Description=''}
        $link | Add-Member -MemberType ScriptMethod -Name Save -Value {
            $global:AirPodsInstallTest.SavedLinks += [pscustomobject]@{Path=$this.Path;TargetPath=$this.TargetPath;WorkingDirectory=$this.WorkingDirectory}
        }
        return $link
    }
    return $shell
}
function Get-Process { [CmdletBinding()]param([string[]]$Name); return @() }
function Start-Process {
    [CmdletBinding()]
    param([string]$FilePath,[string[]]$ArgumentList,[string]$WindowStyle,[switch]$Wait,[string]$WorkingDirectory)
    if ($FilePath -ne $global:AirPodsInstallTest.ExpectedExe) { throw 'Unexpected process launch in installer test.' }
    $global:AirPodsInstallTest.Launches += $FilePath
}

& (Join-Path $sandbox 'Install.ps1') -SkipBuild -BluetoothInstanceId $original.BluetoothInstanceId
$configured = Get-Content -LiteralPath (Join-Path $sandbox 'Data\settings.json') -Raw | ConvertFrom-Json
if ($configured.BluetoothAddress -ne $original.BluetoothAddress -or $configured.StereoEndpointId -ne $original.StereoEndpointId) { throw 'Installer bound the wrong device.' }
if ($global:AirPodsInstallTest.SavedLinks.Count -ne 2 -or $global:AirPodsInstallTest.Launches.Count -ne 1) { throw 'Expected two intercepted shortcuts and one intercepted tray launch.' }
$status = & (Join-Path $sandbox 'App\AirPodsHelper.exe') --status | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $status.RequestAttempted) { throw 'Read-only status failed for installed configuration.' }
[pscustomobject]@{Result='Passed';Sandbox=$sandbox;DeviceIdentity='Matches existing pairing';ShortcutWrites='Two intercepted; no user links changed';TrayLaunch='Intercepted';BluetoothRequests='None';Config='Windows PowerShell UTF-8 configuration successfully read by helper';StrictMode='2, as in recipient setup'} | ConvertTo-Json
exit 0
