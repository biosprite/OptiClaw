# OptiClaw

<p align="center">
  <a href="https://apps.microsoft.com/detail/9NQCMTMP5PNS"><img alt="Download from the Microsoft Store" src="https://img.shields.io/badge/Download-Microsoft_Store-0078D4?style=flat-square&amp;logo=microsoft&amp;logoColor=white"></a>
  <a href="LICENSE"><img alt="GPL v3.0 or later" src="https://img.shields.io/badge/License-GPL_v3.0%2B-2563EB?style=flat-square"></a>
  <a href="PRIVACY.md"><img alt="Privacy policy" src="https://img.shields.io/badge/Privacy-Policy-475569?style=flat-square"></a>
</p>

OptiClaw is an unofficial, touch-friendly [OptiScaler](https://github.com/optiscaler/OptiScaler) client for Windows PCs with Intel Arc graphics, especially MSI Claw handhelds. It lets compatible games use XeSS instead of built-in DLSS or FSR upscaling, even when they lack native XeSS support.

![OptiClaw game library and configuration screen](.github/assets/opticalaw-screenshot.png)

## Features

- Find installed games in Steam and Xbox libraries, or add an executable or folder manually.
- Detect DLSS, FSR, XeSS, and NVIDIA frame-generation files in game directories.
- Download the latest official OptiScaler release and configure Intel XeSS output.
- Configure supported frame-generation inputs, outputs, and multipliers.
- Back up overwritten files, track added files, and restore the previous game files.

## Why use OptiClaw?

XeSS generally delivers better image quality than FSR 3.1. Its AI-based upscaling is optimized for Intel Arc graphics and typically preserves more fine detail, reduces shimmering, and produces a more stable image in motion. Results can still vary by game, resolution, quality mode, and upscaler integration.

OptiClaw handles the required files and configuration, backs up replaced files, and can restore the previous setup if needed.

> [!WARNING]
> Use OptiClaw only with single-player games. OptiScaler uses DLL injection, which may trigger anti-cheat systems and result in an online-service ban.

## Usage

1. Open OptiClaw and add a game executable, scan a folder, or scan the detected Steam and Xbox libraries.
2. Select a game and confirm the detected upscaler files and installation folder.
3. Choose a proxy DLL, then select **Install XeSS**.
4. Launch the game normally. Press `Insert` to open the OptiScaler overlay.
5. Use **Restore** in OptiClaw to remove installed files and recover replaced files.

OptiClaw downloads OptiScaler from its official GitHub repository. It validates the archive contents and checks the release asset's published SHA-256 digest when one is available.

> [!NOTE]
> OptiClaw is not affiliated with, endorsed by, or supported by the OptiScaler project. For game-specific guidance, see the [OptiScaler compatibility list](https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List).

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the Windows 10 or 11 SDK, then run:

```powershell
dotnet test .\tests\OptiClaw.Core.Tests\OptiClaw.Core.Tests.csproj -c Release
dotnet build .\src\OptiClaw\OptiClaw.csproj -c Release -p:Platform=x64
```
