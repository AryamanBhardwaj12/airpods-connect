Set-StrictMode -Version 2

function Assert-PackageFiles {
    param([string]$Root)
    $meta = Get-Content -LiteralPath (Join-Path $Root 'package.json') -Raw | ConvertFrom-Json
    if ($meta.Product -ne 'AirPodsConnectShare' -or $meta.Architecture -ne 'win-x64') { throw 'This is not a supported AirPods Connect package.' }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    foreach ($entry in $meta.Files) {
        if ([IO.Path]::IsPathRooted($entry.Path) -or $entry.Path -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Invalid package path.' }
        $path = [IO.Path]::GetFullPath((Join-Path $Root $entry.Path))
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
        $item = Get-Item -LiteralPath $path -ErrorAction Stop
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid package file.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "Package file changed or is incomplete: $($entry.Path). Extract a fresh copy of the ZIP." }
    }
    return $meta
}

function Test-DesktopRuntime {
    param([string]$DotNetRoot)
    foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
        $path = Join-Path $DotNetRoot ('shared\' + $framework)
        $versions = @(Get-ChildItem -LiteralPath $path -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^8\.0\.\d+$' })
        if ($versions.Count -eq 0) { return $false }
    }
    return $true
}

function Get-PairedAirPods {
    return @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | Where-Object {
        $_.InstanceId -match '^BTHENUM\\DEV_[A-Fa-f0-9]{12}\\' -and $_.FriendlyName -like '*AirPods*'
    })
}

function Select-PairedAirPods {
    param([object[]]$Candidates, [scriptblock]$ReadChoice = { Read-Host 'Type its number and press Enter' })
    if ($Candidates.Count -eq 0) { throw 'No paired AirPods found. Pair them in Windows Settings > Bluetooth & devices first. Their Bluetooth name must include AirPods. Then run SETUP again.' }
    if ($Candidates.Count -eq 1) { Write-Host "Using: $($Candidates[0].FriendlyName)"; return $Candidates[0] }
    Write-Host 'Which AirPods should this icon connect?'
    for ($i=0; $i -lt $Candidates.Count; $i++) { Write-Host ("  {0}. {1}" -f ($i+1), $Candidates[$i].FriendlyName) }
    $answer = & $ReadChoice
    $number = 0
    if (-not [int]::TryParse($answer, [ref]$number) -or $number -lt 1 -or $number -gt $Candidates.Count) { throw 'That number is not in the list. Run SETUP again and choose one of the listed numbers.' }
    return $Candidates[$number-1]
}

function Assert-FirstInstall {
    param([string]$Destination, [string[]]$ShortcutPaths, [object[]]$Processes)
    if (Test-Path -LiteralPath $Destination) { throw "An installation or previous setup already exists at $Destination. Use its UNINSTALL.cmd if needed, then move that folder aside before reinstalling. Existing files have been preserved." }
    if (@($Processes).Count -gt 0) { throw 'AirPods Connect is already running. Exit its tray icon before setting up another copy.' }
    foreach ($path in $ShortcutPaths) {
        if (Test-Path -LiteralPath $path) { throw "An AirPods Connect shortcut already exists: $path. Remove the previous installation using its uninstaller first. No shortcut has been replaced." }
    }
}

function Assert-NoForeignTray {
    param([string]$ExpectedExe, [object[]]$Processes)
    foreach ($process in $Processes) {
        if (-not $process.Path -or -not [string]::Equals($process.Path, $ExpectedExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Another copy of AirPods Connect is running. This uninstaller will not stop it. Exit that copy first.'
        }
    }
}
