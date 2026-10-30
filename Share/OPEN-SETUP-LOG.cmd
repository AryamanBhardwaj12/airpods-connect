@echo off
if exist "%TEMP%\AirPodsConnect-Setup.log" (
  start "" "%SystemRoot%\System32\notepad.exe" "%TEMP%\AirPodsConnect-Setup.log"
) else (
  echo No setup log found. Run SETUP first.
  pause
)
