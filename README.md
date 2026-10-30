# AirPods Connect for Windows

Left-click an AirPods icon beside the Windows clock to connect your paired AirPods. Right-click for disconnect, connection details and Windows Bluetooth settings.

**[Download the runtime-included ZIP](https://github.com/AryamanBhardwaj12/airpods-connect/releases/latest/download/AirPods-Connect-Runtime-Included-Windows-x64-v2.zip)** - recommended; no separate .NET installation needed.

[All release downloads](https://github.com/AryamanBhardwaj12/airpods-connect/releases/latest)

## Set up

For Windows 10 or 11 on Intel/AMD x64. Windows on ARM is not included.

1. Pair your AirPods in Windows **Settings > Bluetooth & devices**. Their Bluetooth name needs to include `AirPods` for setup discovery.
2. Download the ZIP, right-click it and choose **Extract All**.
3. Open the extracted folder and double-click **SETUP.cmd**. Use your normal Windows account. If multiple AirPods are paired, choose yours from the list.
4. Put the AirPods in and left-click the AirPods tray icon to connect. If the icon is hidden, open the up-arrow beside the clock and drag it out.

The icon starts when you sign in. Sign-in alone does not connect your AirPods. If Windows keeps another sound output selected, choose your AirPods in Sound output settings.

The app installs in `%LOCALAPPDATA%\AirPodsConnect`. Downloaded/extracted setup files can be deleted after successful installation.

## If setup fails

Double-click **OPEN-SETUP-LOG.cmd** in the extracted folder. It opens `%TEMP%\AirPodsConnect-Setup.log`. Report the error message; review the log before sharing because it can include your Windows username and device names.

An existing installation is preserved. To reinstall, use **UNINSTALL.cmd** in the installed folder, move that folder aside, then run setup again.

These personal builds are unsigned. If Windows or an organization policy blocks the app, use the normal administrator/support process. Setup does not change Secure Boot or install Bluetooth drivers.

## Downloads

- **Runtime-Included ZIP (~66 MB):** includes .NET 8.0.33 and its notices.
- **Small ZIP (~183 KB):** requires the Windows x64 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) first. An SDK is not needed to use the app.
- **SHA256SUMS.txt:** checksums of both ZIPs.

The v2 launcher fixes Windows PowerShell module discovery when starting from an environment that also has PowerShell 7. It also saves setup errors and detects missing extraction files.

## What it does

Setup selects the exact paired Bluetooth device and its audio endpoint on your computer. Left-click sends a Windows audio reconnect request; right-click **Disconnect audio** requests disconnection. Connection attempts are bounded and duplicate operations are guarded.

This utility does not improve Bluetooth call-mode sound quality or enable simultaneous high-quality stereo with the AirPods microphone. It contains no experimental microphone drivers or bypass.

## Validation

The original connector was tested and accepted on its developer's Windows PC. The v2 packages passed helper identity/ABI/timeout checks, 29 PowerShell fixture checks, extracted-file hash checks and actual CMD-launch regression checks. Installation configuration was also exercised in a sandbox with shortcut writes and app launches intercepted.

Installation and connection on every Windows/Bluetooth combination are not established. The second-laptop retry remains unconfirmed.

## Build from source

On Windows with a .NET SDK capable of building the .NET 8 target:

```powershell
pwsh -File .\Share\Build-Package.ps1 -RuntimeVersion 8.0.33 -ReleaseTag v2
```

This publishes both variants, runs the helper self-tests and writes ZIPs under `Releases/`. Existing release files are preserved rather than overwritten. The runtime-included package retains the .NET license and notice files.

Run the setup fixture checks with Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Share\Test-Package.ps1
```

To create a local framework-dependent developer build, run `Code/Build.ps1`, then `Install.ps1 -SkipBuild` to select a unique paired AirPods device. These developer commands require the .NET SDK and Desktop Runtime.

## Remove

Open `%LOCALAPPDATA%\AirPodsConnect` and run **UNINSTALL.cmd**. It removes this installation's Start-menu/startup links and stops its tray, while retaining files and settings. It refuses to stop another copy running from a different folder.
