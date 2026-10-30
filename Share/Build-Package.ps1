param([string]$RuntimeVersion, [ValidatePattern('^[A-Za-z0-9-]*$')][string]$ReleaseTag = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root ('Data\ShareBuild\' + [guid]::NewGuid().ToString('N'))
$releases = Join-Path $root 'Releases'
New-Item -ItemType Directory -Path $build,$releases -Force | Out-Null
if (-not $RuntimeVersion) {
    $metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json' -TimeoutSec 30
    $RuntimeVersion = $metadata.'latest-runtime'
}
if ($RuntimeVersion -notmatch '^8\.0\.\d+$') { throw 'Expected a stable .NET 8 servicing release.' }
$results = @()
foreach ($bundled in @($true,$false)) {
    $variant = if ($bundled) { 'Runtime-Included' } else { 'Small' }
    $package = Join-Path $build $variant
    $payload = Join-Path $package 'Data\Stage'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    foreach ($project in @('Code\Helper\AirPodsHelper.csproj','Code\Tray\AirPodsConnect.csproj')) {
        $arguments = @('publish',(Join-Path $root $project),'-c','Release','-r','win-x64','--self-contained',$bundled.ToString().ToLowerInvariant(),'-o',$payload,'--nologo','-p:DebugType=None','-p:DebugSymbols=false','-p:SatelliteResourceLanguages=en')
        if ($bundled) { $arguments += "-p:RuntimeFrameworkVersion=$RuntimeVersion" }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project ($variant)" }
    }
    & (Join-Path $payload 'AirPodsHelper.exe') --self-test
    if ($LASTEXITCODE -ne 0) { throw "Published helper tests failed ($variant)." }
    if ($bundled) {
        foreach ($name in @('coreclr.dll','hostpolicy.dll','System.Windows.Forms.dll','System.Windows.Forms.Primitives.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $payload $name))) { throw "Bundled desktop runtime asset missing: $name" }
        }
        foreach ($name in @('AirPodsHelper','AirPodsConnect')) {
            $rc = Get-Content -LiteralPath (Join-Path $payload "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
            if (-not $rc.runtimeOptions.includedFrameworks -or $rc.runtimeOptions.frameworks) { throw "Not self-contained: $name" }
        }
        $desktopRc = Get-Content -LiteralPath (Join-Path $payload 'AirPodsConnect.runtimeconfig.json') -Raw | ConvertFrom-Json
        if (-not @($desktopRc.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq $RuntimeVersion }).Count) { throw 'Unexpected bundled Desktop Runtime version.' }
        $noticeRoot = Join-Path $package 'Notices'
        New-Item -ItemType Directory -Path $noticeRoot | Out-Null
        foreach ($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
            $packRoot = Join-Path $env:USERPROFILE ('.nuget\packages\' + $pack + '\' + $RuntimeVersion)
            $licenses = @(Get-ChildItem -LiteralPath $packRoot -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)' })
            if ($licenses.Count -eq 0) { throw "Runtime license missing: $pack" }
            foreach ($license in $licenses) { Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $noticeRoot ($pack + '-' + $license.Name + '.txt')) }
        }
    }
    # Publish outputs are flat. Reject unplanned content rather than copying a project tree.
    $published = @(Get-ChildItem -LiteralPath $payload -File)
    if (@(Get-ChildItem -LiteralPath $payload -Directory).Count -gt 0) { throw 'Unexpected publish subdirectory; review package allowlist.' }
    foreach ($file in $published) {
        if ($file.Extension -notin @('.dll','.exe','.json','.dat','.bin','.txt','.config')) { throw "Unexpected publish file: $($file.Name)" }
    }
    foreach ($file in @('Setup.ps1','Setup.Core.ps1','SETUP.cmd','Remove.ps1','UNINSTALL.cmd','OPEN-SETUP-LOG.cmd','START HERE.txt')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $package $file)
    }
    foreach ($file in @('Install.ps1','Uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $package $file) }
    $manifest = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
        @{Path=$_.FullName.Substring($package.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    @{Product='AirPodsConnectShare';Architecture='win-x64';BundledRuntime=$bundled;RuntimeVersion=if ($bundled) {$RuntimeVersion} else {'8.0.0 or later 8.0 Desktop'};BuiltUtc=[DateTime]::UtcNow.ToString('O');Files=$manifest} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $package 'package.json') -Encoding utf8
    $suffix = if ($ReleaseTag) { '-' + $ReleaseTag } else { '' }
    $zip = Join-Path $releases "AirPods-Connect-$variant-Windows-x64$suffix.zip"
    if (Test-Path -LiteralPath $zip) { throw "Release already exists; preserve it before rebuilding: $zip" }
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal
    $results += [pscustomobject]@{Variant=$variant;Zip=$zip;Bytes=(Get-Item -LiteralPath $zip).Length;Sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash;Staging=$package}
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $build 'build-result.json') -Encoding utf8
$results | ConvertTo-Json -Depth 4
