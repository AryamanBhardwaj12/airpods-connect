@echo off
setlocal
set "LOG=%TEMP%\AirPodsConnect-Setup.log"
>"%LOG%" echo AirPods Connect setup launch - %DATE% %TIME%
rem Use Windows PowerShell's own modules, even when launched from PowerShell 7.
set "PSModulePath=%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules;%ProgramFiles%\WindowsPowerShell\Modules"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
for %%F in (Setup.ps1 Setup.Core.ps1 Install.ps1 package.json) do (
  if not exist "%~dp0%%F" goto :incomplete
)
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "RESULT=%ERRORLEVEL%"
>>"%LOG%" echo Launcher exit code: %RESULT%
echo.
echo Setup log: %LOG%
echo.
pause
exit /b %RESULT%

:incomplete
>>"%LOG%" echo Setup files are missing. Extract the entire ZIP using Extract All, then open the extracted folder and run SETUP again.
echo Setup files are missing.
echo Right-click the ZIP, choose Extract All, then run SETUP in the extracted folder.
echo Log: %LOG%
pause
exit /b 1
