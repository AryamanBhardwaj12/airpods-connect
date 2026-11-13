# Windows PowerShell 5.1 fixture. Both real shortcut paths are intercepted:
# no COM object, user shortcut write, app launch, or Bluetooth query is allowed.
$ErrorActionPreference = 'Stop'
$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence = Join-Path $project ('Data\IconVisibilityTests\Installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$script:checks = 0
function Assert([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL $name" }
    $script:checks++; Write-Output "PASS $name"
}
function Fingerprint([string]$folder) {
    return (@(Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($folder.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join '|')
}
$realTrayBefore = @(Microsoft.PowerShell.Management\Get-Process AirPodsConnect -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$realPreserved = @((Join-Path $project 'Data\settings.json')) + @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('Programs') | ForEach-Object { Join-Path $_ 'AirPods Connect.lnk' })
$realPreserved = @($realPreserved | Where-Object { Microsoft.PowerShell.Management\Test-Path -LiteralPath $_ -PathType Leaf })
$realHashes = @($realPreserved | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })

function Test-Path {
    [CmdletBinding()]param([string]$LiteralPath, [string]$Path, [string]$PathType = 'Any')
    if ($global:IconInstallerFixture.Links -contains $LiteralPath) { return $PathType -ne 'Container' }
    if ($LiteralPath) { return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType }
    return Microsoft.PowerShell.Management\Test-Path -Path $Path -PathType $PathType
}
function New-Object {
    [CmdletBinding()]param([string]$ComObject)
    if ($ComObject -ne 'WScript.Shell') { throw 'Unexpected COM request' }
    $shell = [pscustomobject]@{}
    $shell | Add-Member ScriptMethod CreateShortcut {
        param($path)
        $f = $global:IconInstallerFixture
        if ($f.Links -notcontains $path) { throw 'Unexpected shortcut path' }
        $f.Reads += $path
        $target = $f.Expected.ToUpperInvariant() # expected target comparison remains case-insensitive
        if ($path -eq $f.Links[1]) {
            if ($f.Case -eq 'ForeignSecond') { $target = 'C:\Foreign\Other.exe' }
            if ($f.Case -eq 'EmptySecond') { $target = '' }
            if ($f.Case -eq 'UnreadableSecond') { throw 'Synthetic unreadable shortcut' }
        }
        $link = [pscustomobject]@{TargetPath=$target;WorkingDirectory='';IconLocation='';Description=''}
        $link | Add-Member ScriptMethod Save { $global:IconInstallerFixture.Saves++; throw 'Unexpected shortcut save' }
        return $link
    }
    return $shell
}
function Start-Process { [CmdletBinding()]param([string]$FilePath,[string[]]$ArgumentList,[string]$WindowStyle,[switch]$Wait,[string]$WorkingDirectory); $global:IconInstallerFixture.Launches++; throw 'Unexpected launch' }
function Get-PnpDevice { [CmdletBinding()]param([string]$Class,[string]$InstanceId); $global:IconInstallerFixture.Hardware++; throw 'Unexpected hardware query' }

foreach ($case in @('ForeignSecond','EmptySecond','UnreadableSecond','ExpectedTargets')) {
    $sandbox = Join-Path $evidence $case
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'App'),(Join-Path $sandbox 'Data\Stage'),(Join-Path $sandbox 'Code') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $project 'Install.ps1') -Destination (Join-Path $sandbox 'Install.ps1')
    foreach ($entry in @('App\AirPodsConnect.exe','Data\settings.json','Data\settings.pending.json','Data\Stage\AirPodsConnect.exe')) {
        [IO.File]::WriteAllText((Join-Path $sandbox $entry), ('original-' + $entry))
    }
    # A real build script proves that a foreign SECOND shortcut refused before
    # optional build. The accepted-target case reaches precisely this sentinel.
    @'
[IO.File]::WriteAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'build-ran.txt'), 'build reached')
throw 'BUILD_SENTINEL'
'@ | Set-Content -LiteralPath (Join-Path $sandbox 'Code\Build.ps1') -Encoding UTF8
    $global:IconInstallerFixture = [pscustomobject]@{
        Case=$case;Expected=(Join-Path $sandbox 'App\AirPodsConnect.exe');
        Links=@([Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('Programs') | ForEach-Object { Join-Path $_ 'AirPods Connect.lnk' });
        Reads=@();Saves=0;Launches=0;Hardware=0
    }
    $appBefore = Fingerprint (Join-Path $sandbox 'App'); $dataBefore = Fingerprint (Join-Path $sandbox 'Data')
    $errorText = ''
    try { & (Join-Path $sandbox 'Install.ps1'); throw 'Installer did not stop at expected boundary' } catch { $errorText = $_.Exception.Message }
    if ($case -eq 'ExpectedTargets') {
        Assert ($errorText -eq 'BUILD_SENTINEL') "$case accepted and reached real build sentinel"
        Assert (Test-Path -LiteralPath (Join-Path $sandbox 'build-ran.txt')) "$case build ran only after both preflight reads"
    } else {
        Assert ($errorText -eq ('Existing shortcut belongs to another app: ' + $global:IconInstallerFixture.Links[1])) "$case exact SECOND shortcut refusal"
        Assert (-not (Test-Path -LiteralPath (Join-Path $sandbox 'build-ran.txt'))) "$case optional build never ran"
    }
    Assert (($global:IconInstallerFixture.Reads -join '|') -eq ($global:IconInstallerFixture.Links -join '|')) "$case both targets read once in order"
    Assert ((Fingerprint (Join-Path $sandbox 'App')) -eq $appBefore) "$case original App unchanged"
    Assert ((Fingerprint (Join-Path $sandbox 'Data')) -eq $dataBefore) "$case Data stage settings and pending config unchanged"
    Assert ($global:IconInstallerFixture.Saves -eq 0 -and $global:IconInstallerFixture.Launches -eq 0 -and $global:IconInstallerFixture.Hardware -eq 0) "$case no first-link save stop launch or hardware query"
}
$realTrayAfter = @(Microsoft.PowerShell.Management\Get-Process AirPodsConnect -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
Assert (($realTrayBefore -join ',') -eq ($realTrayAfter -join ',')) 'real installed tray IDs unchanged'
$realHashesAfter = @($realPreserved | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
@{Paths=$realPreserved;Before=$realHashes;After=$realHashesAfter} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'preservation.json') -Encoding UTF8
Assert (($realHashesAfter -join '|') -eq ($realHashes -join '|')) 'real settings and BOTH shortcuts unchanged'
[pscustomobject]@{Assertions=$script:checks;Result='Passed';Evidence=$evidence;PowerShell=$PSVersionTable.PSVersion.ToString()} | ConvertTo-Json
