param([string]$Sandbox)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Core.ps1')
if (-not $Sandbox) { $Sandbox = Join-Path (Split-Path $PSScriptRoot -Parent) ('Data\ShareTests\' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $Sandbox -Force | Out-Null
$script:checks = 0
function Assert([bool]$Value, [string]$Name) { $script:checks++; if (-not $Value) { throw "FAILED: $Name" } }
function Reject([scriptblock]$Action, [string]$Name) { $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }; Assert $rejected $Name }

# Parse under the actual shipping Windows PowerShell 5.1 host.
foreach ($file in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1')) {
    $tokens=$null; $errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    Assert ($errors.Count -eq 0) ("Parse " + $file.Name)
}

$runtime = Join-Path $Sandbox 'fake-dotnet'
New-Item -ItemType Directory -Path (Join-Path $runtime 'shared\Microsoft.NETCore.App\8.0.33') -Force | Out-Null
Assert (-not (Test-DesktopRuntime $runtime)) 'Base runtime alone is insufficient'
New-Item -ItemType Directory -Path (Join-Path $runtime 'shared\Microsoft.WindowsDesktop.App\9.0.1') -Force | Out-Null
Assert (-not (Test-DesktopRuntime $runtime)) 'Wrong Desktop major is insufficient'
New-Item -ItemType Directory -Path (Join-Path $runtime 'shared\Microsoft.WindowsDesktop.App\8.0.33') -Force | Out-Null
Assert (Test-DesktopRuntime $runtime) 'Desktop plus base accepted'

$a=[pscustomobject]@{FriendlyName='Example AirPods';InstanceId='BTHENUM\DEV_AABBCCDDEEFF\TEST'}
$b=[pscustomobject]@{FriendlyName='Other AirPods';InstanceId='BTHENUM\DEV_112233445566\TEST'}
Assert ((Select-PairedAirPods -Candidates @($a)).InstanceId -eq $a.InstanceId) 'Unique device selected'
Assert ((Select-PairedAirPods -Candidates @($a,$b) -ReadChoice {'2'}).InstanceId -eq $b.InstanceId) 'Explicit second device selected'
Reject { Select-PairedAirPods -Candidates @() } 'No paired device rejected'
Reject { Select-PairedAirPods -Candidates @($a,$b) -ReadChoice {'0'} } 'Out of range rejected'
Reject { Select-PairedAirPods -Candidates @($a,$b) -ReadChoice {'anything'} } 'Non-numeric choice rejected'
function Get-PnpDevice { param($Class,$ErrorAction); @($a,$b,[pscustomobject]@{FriendlyName='AirPods LE';InstanceId='BTHLE\IGNORE'},[pscustomobject]@{FriendlyName='Keyboard';InstanceId='BTHENUM\DEV_111111111111\IGNORE'}) }
Assert (@(Get-PairedAirPods).Count -eq 2) 'Only classic paired AirPods offered'

$destination=Join-Path $Sandbox 'installation'
$shortcut=Join-Path $Sandbox 'AirPods Connect.lnk'
Assert-FirstInstall -Destination $destination -ShortcutPaths @($shortcut) -Processes @()
Assert $true 'Empty installation accepted'
Reject { Assert-FirstInstall $destination @($shortcut) @([pscustomobject]@{Id=1}) } 'Existing process rejected'
'preserve me' | Set-Content -LiteralPath $shortcut
Reject { Assert-FirstInstall $destination @($shortcut) @() } 'Existing shortcut rejected'
Assert ((Get-Content -LiteralPath $shortcut) -eq 'preserve me') 'Existing shortcut preserved'
New-Item -ItemType Directory -Path $destination | Out-Null
Reject { Assert-FirstInstall $destination @() @() } 'Existing destination rejected'
$expected=Join-Path $destination 'App\AirPodsConnect.exe'
Assert-NoForeignTray $expected @([pscustomobject]@{Path=$expected})
Assert $true 'Owned tray accepted'
Reject { Assert-NoForeignTray $expected @([pscustomobject]@{Path='C:\Other\AirPodsConnect.exe'}) } 'Foreign tray rejected'
Reject { Assert-NoForeignTray $expected @([pscustomobject]@{Path=$null}) } 'Unknown tray rejected'

$package=Join-Path $Sandbox 'package'
New-Item -ItemType Directory -Path $package | Out-Null
$file=Join-Path $package 'test.txt'
'expected bytes' | Set-Content -LiteralPath $file
$meta=@{Product='AirPodsConnectShare';Architecture='win-x64';Files=@(@{Path='test.txt';Sha256=(Get-FileHash -LiteralPath $file).Hash})}
$meta | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'package.json')
Assert ((Assert-PackageFiles $package).Product -eq 'AirPodsConnectShare') 'Manifest verifies expected bytes'
'changed bytes' | Set-Content -LiteralPath $file
Reject { Assert-PackageFiles $package } 'Changed payload rejected'
$meta.Files[0].Path='../outside.txt'
$meta | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'package.json')
Reject { Assert-PackageFiles $package } 'Traversal rejected'

# Atomic commit cannot nest a stage into a competing destination.
$stage=Join-Path $Sandbox 'stage'
New-Item -ItemType Directory -Path $stage | Out-Null
Reject { [IO.Directory]::Move($stage,$destination) } 'Concurrent destination cannot be overwritten or nested'
Assert (Test-Path -LiteralPath $stage) 'Rejected commit preserves stage'
Write-Output "$script:checks checks passed. All fixture files are under $Sandbox; no Bluetooth requests, user shortcuts or installations were changed."
