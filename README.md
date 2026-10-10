# GNU Sorter for Windows

A Windows-native rewrite of GNU Sorter built with **WinUI 3** (Windows App SDK) and
.NET 8. It sorts any folder with custom, per-folder rules, and shares the same
JSON configuration format as the Linux version.

## Features

- **Per-folder rules** — every watched folder gets its own ruleset; optionally
  inherit the shared default rules.
- **Split-view rules editor** — folders on the left, rules on the right.
- **Rule dialog** — destination folder plus any combination of extensions and an
  optional name-matching regular expression.
- **Advanced editor** — edit the whole ruleset as JSON.
- **Quick presets** — GNOME defaults · Photography · Code projects · Empty.
- **Background watcher** — watches each folder and sorts new files once they are
  stable, skipping in-progress downloads.
- **Activity + undo** — every move is journaled and can be reversed.
- **Safe by design** — stability window, skip hidden/in-progress/symlinked files,
  and collision-safe destination names (`name (1).ext`).
- **Dry-run mode** — preview what "Sort now" would do without moving anything.
- **Native Windows feel** — Mica backdrop, navigation pane, settings page, and a
  self-contained single-file executable.

## Configuration

Settings are stored under `%APPDATA%\gnu-sorter` and the move journal under
`%LOCALAPPDATA%\gnu-sorter`:

```
%APPDATA%\gnu-sorter\rules.json       default ruleset ("template")
%APPDATA%\gnu-sorter\roots.json       watched folders
%APPDATA%\gnu-sorter\roots.d\*.json   per-folder rulesets
%APPDATA%\gnu-sorter\settings.json    preferences
%LOCALAPPDATA%\gnu-sorter\history.log move journal
```

The schema matches the Linux app (`name`, `extensions`, `match_name_regex`), so an
existing configuration can be reused.

## Download

- **Releases:** https://github.com/notmicrosoft2000-cmd/gnu-sorter-windows/releases
- **CI builds:** triggered on `v*` tags via GitHub Actions; also run manually from
  the Actions tab.

## Build locally (Windows)

Requires the .NET 8 SDK. Building WinUI 3 also works from Visual Studio 2022 with
the **Windows App SDK** workload.

```powershell
git clone https://github.com/notmicrosoft2000-cmd/gnu-sorter-windows.git
cd gnu-sorter-windows

# Debug build
dotnet build gnu-sorter-windows.csproj -c Release -r win-x64 -p:Platform=x64

# Self-contained single-file executable
dotnet publish gnu-sorter-windows.csproj -c Release -r win-x64 -p:Platform=x64 `
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true `
  -p:WindowsPackageType=None -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/gnu-sorter-windows.exe`

## Install / run

Download `gnu-sorter-windows.exe` from Releases and run it. It is a self-contained
single-file executable, so no installer or separate runtime is required. Runs on
Windows 10 1809+ and Windows 11.

## How it works

1. **Add a folder** on the *Folders* page and leave it enabled.
2. Open *Rules*, pick the folder, and either keep the default rules or turn off
   **Use default rules** to give it custom rules. Add rules, apply a preset, or use
   the advanced JSON editor.
3. Press **Watch folders** to sort new files automatically, or **Sort now** to run
   a one-off pass. Review and reverse moves on the *Activity* page.

## License

**GPL-3.0** — same license as GNU Sorter. © 2026 **notmicrosoft2000-cmd**

## Links

- Linux version: https://github.com/notmicrosoft2000-cmd/gnu-sorter
- Website: https://notmicrosoft2000-cmd.github.io/gnu-sorter/
- Issues: https://github.com/notmicrosoft2000-cmd/gnu-sorter/issues
