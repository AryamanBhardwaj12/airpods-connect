param([string]$BluetoothInstanceId, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'App'
$stage = Join-Path $root 'Data\Stage'
$config = Join-Path $root 'Data\settings.json'
$exe = Join-Path $app 'AirPodsConnect.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('Programs')) | ForEach-Object { Join-Path $_ 'AirPods Connect.lnk' }
function Assert-OwnedShortcut([string]$path) {
    if (Test-Path -LiteralPath $path) {
        try { $target = $shell.CreateShortcut($path).TargetPath } catch { throw "Existing shortcut belongs to another app: $path" }
        if ([string]::IsNullOrWhiteSpace($target) -or $target -ne $exe) { throw "Existing shortcut belongs to another app: $path" }
    }
}
# Read BOTH targets before build, stopping a tray, or changing files/config/links.
foreach ($path in $shortcutPaths) { Assert-OwnedShortcut $path }
if (-not $SkipBuild) { & (Join-Path $root 'Code\Build.ps1') }
if (-not (Test-Path -LiteralPath (Join-Path $stage 'AirPodsConnect.exe'))) { throw 'Build not found. Run Code\Build.ps1 first.' }
if (-not $BluetoothInstanceId) {
    $parents = @(Get-PnpDevice -Class Bluetooth | Where-Object { $_.InstanceId -match '^BTHENUM\\DEV_[A-Fa-f0-9]{12}\\' -and $_.FriendlyName -like '*AirPods*' })
    if ($parents.Count -ne 1) { throw 'Expected exactly one paired AirPods device. Pass -BluetoothInstanceId for the intended device.' }
    $BluetoothInstanceId = $parents[0].InstanceId
}
$parent = Get-PnpDevice -InstanceId $BluetoothInstanceId
if ($BluetoothInstanceId -notmatch '^BTHENUM\\DEV_([A-Fa-f0-9]{12})\\') { throw 'A classic paired Bluetooth parent is required.' }
$address = $Matches[1]
$container = [guid](Get-PnpDeviceProperty -InstanceId $BluetoothInstanceId -KeyName DEVPKEY_Device_ContainerId).Data
# Use an intentionally absent config for inventory; neither discovery nor startup reconnects.
$inventory = & (Join-Path $stage 'AirPodsHelper.exe') --list --config (Join-Path $root 'Data\inventory-no-settings.json') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw $inventory.Message }
$stereo = @($inventory.Endpoints | Where-Object { [guid]$_.ContainerId -eq $container -and $_.State -in @(1,8) -and $_.FilterId -match 'BTHENUM.*0000110b' -and $_.FilterId -like "*$address*" })
if ($stereo.Count -ne 1) { $inventory | ConvertTo-Json -Depth 5; throw 'Cannot resolve exactly one matching paired A2DP stereo endpoint.' }
$settings = @{ContainerId=$container.ToString();StereoEndpointId=$stereo[0].Id;BluetoothInstanceId=$BluetoothInstanceId;BluetoothAddress=$address;DisplayName=$parent.FriendlyName}
$tempConfig = Join-Path $root 'Data\settings.pending.json'
$settings | ConvertTo-Json | Set-Content -LiteralPath $tempConfig -Encoding utf8
$check = & (Join-Path $stage 'AirPodsHelper.exe') --status --config $tempConfig | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw $check.Message }
# Retain previous publish output until the staged replacement and config have validated.
if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait }
$running = @(Get-Process AirPodsConnect,AirPodsHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (Split-Path $_.Path -Parent) -eq $app })
foreach ($p in $running) { if (-not $p.WaitForExit(4000)) { throw 'An app operation is still running. Wait and retry installation.' } }
if (Test-Path -LiteralPath $app) {
    $resolved = (Resolve-Path -LiteralPath $app).Path
    if ($resolved -ne (Join-Path $root 'App')) { throw 'Unexpected installation path' }
    $backup = Join-Path $root ('Data\Previous-' + (Get-Date -Format yyyyMMdd-HHmmss))
    Move-Item -LiteralPath $resolved -Destination $backup
}
New-Item -ItemType Directory -Path $app -Force | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $app -Recurse
Move-Item -LiteralPath $tempConfig -Destination $config -Force
foreach ($path in $shortcutPaths) {
    Assert-OwnedShortcut $path
    $shortcut = $shell.CreateShortcut($path)
    if ((Test-Path -LiteralPath $path) -and ([string]::IsNullOrWhiteSpace($shortcut.TargetPath) -or $shortcut.TargetPath -ne $exe)) { throw "Existing shortcut belongs to another app: $path" }
    $shortcut.TargetPath = $exe; $shortcut.WorkingDirectory = $app; $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Left-click the AirPods tray icon to connect'; $shortcut.Save()
}
Start-Process -FilePath $exe -WorkingDirectory $app -WindowStyle Hidden
Write-Output "Installed for $($parent.FriendlyName). Startup and Start menu shortcuts created."
