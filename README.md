# vokoAVALONIA

Proyek ini dibuat secara independen dengan vibe coding dan terinspirasi oleh [vokoscreenNG](https://github.com/vkohaupt/vokoscreenNG), aplikasi perekam layar sumber terbuka.

## Inspirasi dan atribusi

Terima kasih kepada pembuat dan kontributor [vokoscreenNG](https://github.com/vkohaupt/vokoscreenNG) atas karya yang menjadi inspirasi proyek ini. vokoAVALONIA adalah proyek independen: kode vokoscreenNG tidak disalin atau diubah untuk membuat proyek ini. vokoAVALONIA tidak berafiliasi dengan atau didukung secara resmi oleh vokoscreenNG maupun pembuatnya.

Lisensi vokoscreenNG: [GNU General Public License v2.0](https://github.com/vkohaupt/vokoscreenNG/blob/3.0/LICENSE).

---

## English

A simple screen recorder for Windows with a Windows 11 look, written in C# with
[Avalonia UI](https://avaloniaui.net) and [FluentAvalonia](https://github.com/amwx/FluentAvalonia).
Inspired by [vokoscreenNG](https://github.com/vkohaupt/vokoscreenNG) by Volker Kohaupt.

The code is kept deliberately small and plain (KISS), so it is easy to read, fix and extend.

## Features (v0.1)

- Record a whole display, with or without the mouse cursor
- Record a microphone
- MKV or MP4, choice of frame rate and quality
- Pause and continue
- Snapshot (PNG)
- Global shortcuts that work while other apps are in front:
  **Ctrl+Alt+R** start, then pause / continue · **Ctrl+Alt+S** stop · **Ctrl+Alt+C** snapshot
  (letters can be changed in the app)
- Light, dark, or follow Windows

## Download

Open the **Actions** tab, pick the latest successful run, and download
**VokoAvalonia-windows-x64** under *Artifacts* (or use the **Releases** page).
Unzip and run `VokoAvalonia.exe`. `ffmpeg.exe` is included in the zip and must stay next to it.

## Build it yourself

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Run the app: `dotnet run --project src`
3. Make a single .exe:
   ```
   dotnet publish src/VokoAvalonia.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
   ```
4. Put `ffmpeg.exe` (for example from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases)) next to `VokoAvalonia.exe`.

Any editor works: Visual Studio, VS Code (with the C# Dev Kit and Avalonia extensions) or Rider.

## How the code is organised

Everything is in `src/`. Each file does one job:

| File | What it does |
|---|---|
| `Program.cs` | Starts the app. |
| `App.axaml` / `App.axaml.cs` | Loads the Windows 11 theme and icons; switches light / dark. |
| `MainWindow.axaml` | The window layout (cards, buttons). No logic. |
| `MainWindow.axaml.cs` | What the buttons and settings do. |
| `Recorder.cs` | Builds ffmpeg command lines; starts, pauses, stops and joins recordings. |
| `Hotkeys.cs` | Global keyboard shortcuts (Windows `RegisterHotKey`). |
| `Settings.cs` | All settings with their defaults; saved as JSON in `%AppData%\VokoAvalonia`. |
| `Icons.axaml` | Icons from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons). |

**How recording works:** the app does not capture the screen itself. It runs `ffmpeg`
with the right options (`gdigrab` for the screen, `dshow` for the microphone, `libx264` for video).
ffmpeg cannot pause, so each pause ends a piece of video and *Continue* starts a new one;
*Stop* joins the pieces into one file.

## Common changes

- **New setting:** add a property to `Settings.cs`, a card to `MainWindow.axaml`,
  and one line each in `ShowSettings()` and `ReadSettings()` in `MainWindow.axaml.cs`.
- **New choice in a list** (for example 120 fps): add a line to the option arrays at the
  top of `MainWindow.axaml.cs`.
- **Different encoder or capture method:** change `StartSegment()` / `ScreenInput()` in `Recorder.cs`.
- **New icon:** see the comment at the top of `Icons.axaml`.

## Ideas for later

Record a window or an area · system sound · countdown · camera overlay · show clicks /
halo · magnifier · timer · tray icon · Linux (Wayland) support.

## License

Not chosen yet. A license must be added before others can legally reuse this code;
GPL is a natural fit given the vokoscreenNG roots, and ffmpeg builds bundled in releases are GPL.
