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

🇪🇸 [Leer en español](#espanol)

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
| **Default curve overlay** | While the motherboard (not FanCurve) controls a fan, FanCurve logs its temperature/speed behaviour. Tick *Show default curve* to see it as a thin dotted black line behind your blue custom curve. |
| **Selectable sampling rate** | Choose how often temperatures are read (0.25 s to 5 s) from the UI. |
| **Selectable reaction time** | Per rule, choose how fast the fan follows the temperature when it rises and when it falls: Immediate, 0.5 s, 1 s or 2 s. Slower values stop fans from hunting up and down. |
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

Rules and settings (including the sampling rate and language) are stored in `%APPDATA%\FanCurve\config.json` and edited from the UI. The only advanced option is:

| Key | Default | Description |
|---|---|---|
| `FailsafePercent` | `100` | Fan speed used if a rule's temperature sensor stops reporting. |

## Limitations

- Fan **control** depends on your hardware being supported by LibreHardwareMonitor (most Nuvoton/ITE Super I/O chips, many GPUs). If no *Control* channels appear in the Sensors tab, FanCurve can't drive your fans.
- If the process is killed forcefully (e.g. *End task*), fans stay at the last written speed until FanCurve is started again or the PC restarts. A normal exit restores automatic control.
- The UI is available in English and Spanish (language selector at the bottom of the window; it defaults to your system language).

## Safety

Controlling fans manually can cause **overheating and hardware damage** if configured incorrectly. FanCurve warns about risky curves but cannot know which fan is connected to which header. Check your curves, monitor temperatures after changing them, and use at your own risk. See the [license](LICENSE).

## Acknowledgements

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for the sensor and fan-control layer.
- [PawnIO](https://pawnio.eu/) for the signed low-level driver.

## License

[MIT](LICENSE) © FanCurve contributors. Third-party components keep their own licenses (LibreHardwareMonitorLib: MPL-2.0).

---

<a id="espanol"></a>

<div align="center">

# FanCurve (Español)

**Un controlador de curvas de ventiladores para Windows, mínimo y sin vueltas.**<br>
Hace una sola cosa: relacionar una temperatura con una velocidad de ventilador, correr en la bandeja del sistema e iniciar con Windows.

</div>

## ¿Por qué?

Las suites de control de ventiladores de los fabricantes suelen pesar cientos de megabytes, traen decenas de funciones que nunca usás y se actualizan constantemente.
FanCurve hace **solo** curvas de ventiladores:

- **~1 MB** en una carpeta portable. Sin instalador y sin runtime que instalar (usa el .NET Framework 4.8 que ya trae Windows).
- **Cualquier fuente de temperatura para cualquier salida de ventilador.** Controlá un ventilador de gabinete con el *hot spot* de la GPU, el cooler del CPU con la temperatura del paquete del CPU, o la combinación que quieras.
- **Corre en segundo plano** desde el inicio de sesión, oculto en la bandeja del sistema.
- **Compilación reproducible en Docker.** No necesitás instalar compiladores, SDKs ni lenguajes en tu PC.

## Características

| | |
|---|---|
| **Editor visual de curvas** | Arrastrá los puntos, doble clic para agregar uno, clic derecho para quitarlo. Un marcador en vivo muestra la temperatura actual y la velocidad resultante. |
| **Asignación flexible** | Elegí cualquier sensor de temperatura detectado (CPU, núcleo de GPU, hot spot de GPU, placa madre, discos…) para cualquier salida de ventilador controlable. |
| **Varias reglas guardadas** | Guardá curvas alternativas para el mismo ventilador. Solo una regla por salida puede estar activa a la vez: FanCurve te avisa y desactiva la otra. |
| **Controles de seguridad** | Advierte antes de aplicar una curva que deje un ventilador por debajo del 30% con más de 70 °C. Pasa al 100% si el sensor deja de informar. Devuelve el control a la placa madre al salir. |
| **Curva por defecto superpuesta** | Mientras la placa (y no FanCurve) controla un ventilador, FanCurve registra su comportamiento temperatura/velocidad. Tildá *Mostrar curva por defecto* para verlo como una línea negra punteada fina detrás de tu curva azul personalizada. |
| **Frecuencia de muestreo seleccionable** | Elegí cada cuánto se leen las temperaturas (de 0,25 s a 5 s) desde la interfaz. |
| **Tiempo de reacción seleccionable** | Por regla, elegí qué tan rápido sigue el ventilador a la temperatura cuando sube y cuando baja: Inmediato, 0,5 s, 1 s o 2 s. Valores más lentos evitan que los ventiladores suban y bajen constantemente. |
| **Explorador de sensores** | Mirá todas las temperaturas, RPM y canales de control, y probá un canal al 100% para saber qué ventilador físico maneja. |
| **Inicio automático** | Una casilla crea una tarea programada con privilegios elevados (sin aviso de UAC en cada inicio de sesión). |

## Inicio rápido

1. Instalá una sola vez el driver [PawnIO](https://pawnio.eu/) (lo requiere la capa de hardware):
   ```powershell
   winget install namazso.PawnIO
   ```
2. Descargá o [compilá](#compilar-desde-el-código-fuente) `FanCurve-<versión>-win-x64.zip`, extraelo donde quieras y ejecutá `FanCurve.exe` (pide permisos de administrador).
3. En la pestaña **Sensores**, seleccioná una fila *Control* y presioná **Probar salida al 100%** para ver qué ventilador reacciona.
4. En la pestaña **Reglas**, presioná **Agregar regla**, elegí la salida del ventilador y la fuente de temperatura, y dale forma a la curva.
5. Presioná **Guardar y aplicar**, y marcá **Iniciar con Windows** si querés que se ejecute en cada inicio de sesión.

> **Ojo:** cerrá o desinstalá otros controladores de ventiladores (Armoury Crate, iCUE, MSI Center, FanControl, …) mientras uses FanCurve. Dos programas escribiendo en el mismo ventilador se pelean entre sí.

## Cómo funciona

```
 ┌────────────────┐   temperatura   ┌──────────────┐  objetivo %  ┌─────────────────────┐
 │ Sensor (CPU,   │ ───────────────▶│   Curva de   │─────────────▶│ Canal de control    │
 │ hot spot GPU…) │   suavizado     │ ventilador   │              │ (Super I/O, GPU…)   │
 └────────────────┘                 │ (interpola-  │              └─────────────────────┘
                                    │  ción lineal)│
                                    └──────────────┘
```

FanCurve lee los sensores y escribe las velocidades mediante [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), la misma capa de hardware que usa LibreHardwareMonitor.
Una vez por segundo (configurable), cada regla activa evalúa su curva y aplica el resultado a su salida. Las salidas que dejan de estar controladas vuelven al control automático de la placa madre.

## Compilar desde el código fuente

El único requisito es **Docker** (con Compose). No se instala nada más en tu PC.

```powershell
docker compose run --rm build
```

El resultado queda en `dist/`:

```
dist/
├── FanCurve.exe
├── LibreHardwareMonitorLib.dll
├── …(dependencias)
└── FanCurve-<versión>-win-x64.zip   # mismo contenido, listo para compartir
```

La compilación ejecuta `dotnet publish` dentro de la imagen oficial del SDK de .NET, compilando de forma cruzada un ejecutable `net48` de Windows desde Linux.

## Versiones y lanzamientos

FanCurve sigue el [Versionado Semántico](https://semver.org/lang/es/): `MAJOR.MINOR.PATCH`.

- **PATCH**: corrección de errores. **MINOR**: funciones nuevas compatibles hacia atrás. **MAJOR**: cambios incompatibles (por ejemplo, el formato del archivo de configuración).
- Las versiones preliminares llevan un sufijo: `v1.2.0-rc.1`, `v2.0.0-beta.1`. Se publican como *pre-release* en GitHub.

Para publicar una versión, creá un tag sobre un commit de `main` y subilo:

```bash
git tag v1.0.0
git push origin v1.0.0
```

El [workflow de Release](.github/workflows/release.yml) compila el proyecto en Docker, incrusta la versión en el ejecutable y adjunta `FanCurve-<versión>-win-x64.zip` y `SHA256SUMS.txt` a un nuevo Release de GitHub con notas generadas automáticamente.
Cada push a `main` y cada pull request también se compilan con el [workflow de CI](.github/workflows/ci.yml).

## Configuración

Las reglas y los ajustes (incluidos la frecuencia de muestreo y el idioma) se guardan en `%APPDATA%\FanCurve\config.json` y se editan desde la interfaz. La única opción avanzada es:

| Clave | Valor por defecto | Descripción |
|---|---|---|
| `FailsafePercent` | `100` | Velocidad usada si el sensor de temperatura de una regla deja de informar. |

## Limitaciones

- El **control** de ventiladores depende de que tu hardware sea compatible con LibreHardwareMonitor (la mayoría de los chips Super I/O Nuvoton/ITE y muchas GPUs). Si en la pestaña Sensores no aparece ningún canal *Control*, FanCurve no puede manejar tus ventiladores.
- Si el proceso se cierra a la fuerza (por ejemplo, *Finalizar tarea*), los ventiladores quedan en la última velocidad escrita hasta que FanCurve vuelva a iniciarse o se reinicie la PC. Un cierre normal restaura el control automático.
- La interfaz está disponible en inglés y español (selector de idioma abajo en la ventana; por defecto usa el idioma del sistema).

## Seguridad

Controlar ventiladores manualmente puede causar **sobrecalentamiento y daños en el hardware** si se configura mal. FanCurve advierte sobre curvas riesgosas, pero no puede saber qué ventilador está conectado a cada conector. Revisá tus curvas, monitoreá las temperaturas después de cambiarlas y usalo bajo tu propia responsabilidad. Ver la [licencia](LICENSE).

## Agradecimientos

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) por la capa de sensores y control de ventiladores.
- [PawnIO](https://pawnio.eu/) por el driver firmado de bajo nivel.

## Licencia

[MIT](LICENSE) © FanCurve contributors. Los componentes de terceros conservan sus propias licencias (LibreHardwareMonitorLib: MPL-2.0).
