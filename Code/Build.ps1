$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'Data\Stage'
& (Join-Path $PSScriptRoot 'Create-Icon.ps1')
foreach ($project in @('Helper\AirPodsHelper.csproj','Tray\AirPodsConnect.csproj')) {
    & dotnet publish (Join-Path $PSScriptRoot $project) -c Release -r win-x64 --self-contained false -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
& (Join-Path $stage 'AirPodsHelper.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Helper self-tests failed' }
Write-Output "Validated build: $stage"
