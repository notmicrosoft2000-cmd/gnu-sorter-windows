# GNU Sorter for Windows

Windows-native version of GNU Sorter with **WinUI 3** (Windows App SDK) for a clean Fluent Design UX.

**Same purpose:** sort any folder(s) with custom rules (per-folder independent rules or inherit the default template).  
**Different UI:** designed specifically for Windows 10/11.

## Features

- **Per-folder rules** — each watched folder gets its own ruleset; optionally inherit defaults
- **Split-view rules editor** — folders left, rules right (Fluent UI)
- **Add Rule wizard** — guided step-by-step
- **Advanced editor** — multi-extensions, name contains/regex, destination
- **Quick presets** — GNOME defaults · Photography · Code projects · Empty
- **Activity + Undo** — journaled moves with undo
- **Safe sorting** — stability window, skip in-progress files, collision-safe names
- **Native Windows feel** — Mica backdrop, top navigation, Settings, About

## Download

- **GitHub Releases:** https://github.com/notmicrosoft2000-cmd/gnu-sorter-windows/releases
- **CI builds:** triggered on `v*` tags via GitHub Actions; also run manually from Actions tab

## Build locally (Windows)

Requires Visual Studio 2022 with **Windows App SDK** workload, or .NET 8 SDK + MSBuild.

```powershell
# Clone
git clone https://github.com/notmicrosoft2000-cmd/gnu-sorter-windows.git
cd gnu-sorter-windows

# Build x64 (self-contained, single-file)
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:WindowsPackageType=None -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/gnu-sorter-windows.exe`

## Install/Run

Download `gnu-sorter-windows.exe` from Releases — it's a self-contained single-file executable. No installer required (just run it). Runs on Windows 10 1809+ / Windows 11.

## License

**GPL-3.0** — same license as GNU Sorter. © 2026 **notmicrosoft2000-cmd**

## Links

- Linux version: https://github.com/notmicrosoft2000-cmd/gnu-sorter
- Website: https://notmicrosoft2000-cmd.github.io/gnu-sorter/
