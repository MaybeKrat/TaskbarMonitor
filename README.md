# TaskbarMonitor

[Русская версия](README.ru.md)

A compact CPU, RAM and GPU monitor that sits right on the Windows 11 taskbar, next to the system tray.
Hover over it to see the details.

![Details on hover](docs/details_en.png)

![On the taskbar](docs/panel.png)

## Features

- Load rings for CPU, RAM and GPU, updated twice a second. Values are smoothed and the rings move
  continuously, so nothing flickers.
- One value under each label, chosen in the right-click menu: CPU clock or temperature, used or free
  RAM, GPU power or temperature.
- Hover card: CPU load, clock, temperature and power; RAM load, used and free; GPU load, clock,
  temperature, power and its limit, VRAM, fans. Temperatures are colored from normal to red.
- Click opens Task Manager. Right-click menu: start with Windows, values, exit.
- Light and dark taskbar, any resolution and display scale. English or Russian, following Windows.
- No administrator rights required.

## Requirements

- Windows 11 with the taskbar at the bottom; the panel appears on the main display.
- NVIDIA graphics card for GPU data (read from the driver via NVML).
- MSI Afterburner running — only for CPU temperature and power. Without it those fields show “—”.

## Download

Download `TaskbarMonitor.exe` from [Releases](https://github.com/MaybeKrat/TaskbarMonitor/releases/latest)
and run it. It is a single self-contained file: .NET does not need to be installed.

Settings are stored in `%APPDATA%\TaskbarMonitor\settings.json`, errors are logged to `error.log`
next to the exe.

## Building

Requires the .NET 10 SDK. The release exe is built with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

The exe is not compressed on purpose: a compressed one is half the size but takes about 40 MB more memory
while running, because .NET unpacks itself into RAM.

`publish.ps1` builds, publishes to a local folder and restarts the running copy — handy while making changes.

## How it works

Windows 11 has no API for taskbar widgets, so the panel is a transparent child window placed inside
the taskbar left of the tray. It is drawn with WPF and shown through a per-pixel-alpha layered window.

| File | What it does |
|---|---|
| `src/Program.cs` | Entry point: single instance, sensor polling every 500 ms, smoothing |
| `src/Sensors.cs`, `src/Afterburner.cs` | Sensors: Windows performance counters, NVML, MSI Afterburner shared memory |
| `src/TaskbarPanel.cs` | The window inside the taskbar: ring animation, hover, click, menu |
| `src/PanelRenderer.cs` | How the panel looks |
| `src/DetailsFlyout.cs`, `src/DetailsRenderer.cs` | Hover card: the window and how it looks |
| `src/Theme.cs`, `src/Strings.cs` | Light and dark colors, temperature colors; English and Russian texts |
| `src/Settings.cs`, `src/Autostart.cs` | Settings from the menu and start with Windows |
| `src/Paint.cs`, `src/LayeredCanvas.cs` | Drawing helpers and output to a transparent window |
| `src/Native.cs`, `src/Log.cs` | Windows API calls; error log |

Sizes in the renderers are in pixels for a 125% display scale and are recalculated for the actual one.
