<div align="center">

# FanCurve

**A tiny, no-nonsense fan curve controller for Windows.**<br>
One job: map a temperature sensor to a fan speed, run quietly in the tray, start with Windows.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet&logoColor=white)
![Size](https://img.shields.io/badge/download-~1%20MB-brightgreen)
![Build](https://img.shields.io/badge/build-Docker-2496ED?logo=docker&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-blue)

</div>

---

## Why?

Vendor fan-control suites are often hundreds of megabytes, ship dozens of features you never touch, and update constantly.
FanCurve does **only** fan curves:

- **~1 MB** portable folder. No installer, no runtime to install (uses the .NET Framework 4.8 that ships with Windows).
- **Any temperature source for any fan output.** Drive a case fan from the GPU *hot spot*, the CPU fan from the CPU package temperature, or whatever combination you like.
- **Runs in the background** from logon, hidden in the system tray.
- **Reproducible build in Docker.** You never have to install a compiler, SDK or language on your machine.

## Features

| | |
|---|---|
| **Visual curve editor** | Drag points, double-click to add, right-click to remove. A live marker shows the current temperature and resulting speed. |
| **Flexible mapping** | Pick any detected temperature sensor (CPU, GPU core, GPU hot spot, motherboard, drives…) for any controllable fan output. |
| **Multiple saved rules** | Keep alternative curves for the same fan. Only one rule per output can be active at a time; FanCurve warns you and switches the other one off. |
| **Safety checks** | Warns before applying a curve that runs a fan below 30% above 70 °C. Falls back to 100% if the sensor stops reporting. Restores motherboard control on exit. |
| **Smooth behaviour** | Reacts instantly to rising temperatures and ramps down gradually, so fans don't hunt up and down. |
| **Sensor browser** | See every temperature, RPM and control channel, and test a channel at 100% to find out which physical fan it drives. |
| **Autostart** | One checkbox creates an elevated scheduled task (no UAC prompt at every logon). |

## Quick start

1. Install the [PawnIO](https://pawnio.eu/) driver once (required by the hardware layer):
   ```powershell
   winget install namazso.PawnIO
   ```
2. Download or [build](#building-from-source) `FanCurve-portable.zip`, extract it anywhere and run `FanCurve.exe` (it asks for administrator rights).
3. Open the **Sensors** tab, select a *Control* row and press **Test output at 100%** to see which fan reacts.
4. In the **Rules** tab press **Add rule**, choose the fan output and the temperature source, and shape the curve.
5. Press **Save and apply**, and tick **Start with Windows** if you want it to run at every logon.

> **Heads-up:** close or uninstall other fan controllers (Armoury Crate, iCUE, MSI Center, FanControl, …) while using FanCurve. Two programs writing to the same fan will fight each other.

## How it works

```
 ┌────────────────┐   temperature   ┌──────────────┐   target %   ┌─────────────────────┐
 │ Sensor (CPU,   │ ───────────────▶│  Fan curve   │─────────────▶│ Fan control channel │
 │ GPU hot spot…) │   smoothing     │ (linear      │              │ (Super I/O, GPU…)   │
 └────────────────┘                 │ interpolate) │              └─────────────────────┘
                                    └──────────────┘
```

FanCurve reads sensors and writes fan speeds through [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), the same hardware layer used by LibreHardwareMonitor.
Once per second (configurable) each active rule evaluates its curve and applies the result to its output. Outputs that are no longer driven return to the motherboard's automatic control.

## Building from source

The only requirement is **Docker** (with Compose). Nothing else is installed on the host.

```powershell
docker compose run --rm build
```

The result lands in `dist/`:

```
dist/
├── FanCurve.exe
├── LibreHardwareMonitorLib.dll
├── …(dependencies)
└── FanCurve-<version>-win-x64.zip   # same content, ready to share
```

The build runs `dotnet publish` inside the official .NET SDK image, cross-compiling a Windows `net48` executable on Linux.

## Releases and versioning

FanCurve follows [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.

- **PATCH** – bug fixes. **MINOR** – new backwards-compatible features. **MAJOR** – breaking changes (e.g. config format).
- Pre-releases use a suffix: `v1.2.0-rc.1`, `v2.0.0-beta.1`. They are published as *pre-release* on GitHub.

To cut a release, tag a commit on `main` and push the tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The [Release workflow](.github/workflows/release.yml) then builds the project in Docker, embeds the version in the executable and attaches `FanCurve-<version>-win-x64.zip` plus `SHA256SUMS.txt` to a new GitHub Release with auto-generated notes.
Every push to `main` and every pull request is also built by the [CI workflow](.github/workflows/ci.yml).

## Configuration

Rules are stored in `%APPDATA%\FanCurve\config.json` and edited from the UI. Advanced options:

| Key | Default | Description |
|---|---|---|
| `IntervalMs` | `1000` | How often sensors are read and fans updated (minimum 250). |
| `FailsafePercent` | `100` | Fan speed used if a rule's temperature sensor stops reporting. |

## Limitations

- Fan **control** depends on your hardware being supported by LibreHardwareMonitor (most Nuvoton/ITE Super I/O chips, many GPUs). If no *Control* channels appear in the Sensors tab, FanCurve can't drive your fans.
- If the process is killed forcefully (e.g. *End task*), fans stay at the last written speed until FanCurve is started again or the PC restarts. A normal exit restores automatic control.
- The UI is currently in Spanish.

## Safety

Controlling fans manually can cause **overheating and hardware damage** if configured incorrectly. FanCurve warns about risky curves but cannot know which fan is connected to which header. Check your curves, monitor temperatures after changing them, and use at your own risk. See the [license](LICENSE).

## Acknowledgements

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for the sensor and fan-control layer.
- [PawnIO](https://pawnio.eu/) for the signed low-level driver.

## License

[MIT](LICENSE) © FanCurve contributors. Third-party components keep their own licenses (LibreHardwareMonitorLib: MPL-2.0).
