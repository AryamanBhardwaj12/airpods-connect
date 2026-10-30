$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'App'
$exe = Join-Path $app 'AirPodsConnect.exe'
if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait }
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('Programs'))) {
    $path = Join-Path $folder 'AirPods Connect.lnk'
    if ((Test-Path -LiteralPath $path) -and $shell.CreateShortcut($path).TargetPath -eq $exe) { Remove-Item -LiteralPath $path }
}
Write-Output 'AirPods Connect removed from startup and Start menu. Source, app binaries and settings retained in this folder.'
