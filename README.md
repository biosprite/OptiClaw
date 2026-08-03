# OptiClaw

OptiClaw is an unofficial, handheld-friendly WinUI 3 client for installing [OptiScaler](https://github.com/optiscaler/OptiScaler) into individual Windows games. It is designed around Intel Arc handhelds such as the MSI Claw and configures DirectX 11, DirectX 12, and Vulkan output to Intel XeSS.

> [!IMPORTANT]
> OptiClaw is not made, endorsed, or supported by the OptiScaler team. OptiScaler explicitly has no official manager app. Only download OptiScaler from its [official GitHub repository](https://github.com/optiscaler/OptiScaler).

## What works

- Add any game by selecting its `.exe`.
- Scan Steam and accessible Xbox game libraries.
- Scan a custom library folder.
- Detect DLSS, FSR/FidelityFX, XeSS, and NVIDIA Streamline DLLs.
- Prefer the real Unreal Engine `Win64-Shipping.exe` instead of a launcher.
- Download the latest official OptiScaler `.7z` release on demand.
- Verify the release asset against GitHub's SHA-256 digest before extraction.
- Install beside the real game executable using `dxgi.dll` by default, with every proxy name currently supported by OptiScaler available in the UI.
- Set `Dx11Upscaler`, `Dx12Upscaler`, and `VulkanUpscaler` to `xess` in `OptiScaler.ini`.
- Back up every overwritten file and track every added file in a per-game manifest.
- Restore the original game folder in one click. Restore stops safely if an installed file was changed after setup.

OptiClaw does not patch game executables, bypass anti-cheat, or inject into a running process. It only manages the official OptiScaler files on disk.

## Use it

1. Download the latest `OptiClaw.exe`, or build it below.
2. Run `OptiClaw.exe`.
3. Select **Scan libraries**, **Scan folder**, or **Add game**.
4. Check the detected game executable and install folder. For Unreal games this should normally end in `Binaries\Win64` or `Binaries\WinGDK`.
5. Leave the proxy at `dxgi.dll` unless the game's [OptiScaler compatibility notes](https://github.com/optiscaler/OptiScaler/wiki) recommend another filename.
6. Select **Install XeSS** and confirm the exact target.
7. Start the game normally. Press `Insert` in game to open the OptiScaler overlay.

Use **Restore** before verifying game files, uninstalling the game, changing proxy DLLs, or removing the game from OptiClaw.

> [!WARNING]
> Use DLL-based mods only in games that permit them. Anti-cheat software can flag modified game folders. OptiClaw is intended for single-player use and provides no anti-cheat workaround.

## Build

Requirements:

- Windows 10 1809 or newer (Windows 11 recommended)
- .NET 10 SDK
- Windows 10/11 SDK

From PowerShell:

```powershell
dotnet test .\tests\OptiClaw.Core.Tests\OptiClaw.Core.Tests.csproj -c Release
dotnet build .\src\OptiClaw\OptiClaw.csproj -c Release -p:Platform=x64
```

To create a compressed, self-contained Windows App SDK executable:

```powershell
.\scripts\build.ps1
```

The single-file build is written to `artifacts\OptiClaw.exe`. The app is unpackaged and carries its Windows App SDK/.NET runtime inside the executable, so no separate runtime installation is required. Windows extracts those dependencies to a temporary application cache when the app starts.

## How installs stay recoverable

OptiClaw stores settings, release cache, manifests, and original-file backups under:

```text
%LOCALAPPDATA%\OptiClaw
```

Writes use a temporary file in the destination directory followed by an atomic replace. If setup fails partway through, completed changes are rolled back. Restore first verifies that installed files still match the recorded hashes; it refuses to destroy later user/game changes.

The OptiScaler binary is intentionally not checked into this repository. OptiClaw resolves the latest release through the official GitHub API, validates the asset digest, and caches the verified payload locally. This avoids shipping stale third-party binaries while retaining upstream license files from the release archive.

## Project layout

```text
src/OptiClaw          WinUI 3 desktop client
src/OptiClaw.Core     Discovery, download, install, backup, and restore logic
tests                 Fast file-system tests for the safety-critical core
scripts               Local publish helper
```

OptiClaw is licensed under MIT. OptiScaler and the libraries bundled in its releases retain their own licenses.

