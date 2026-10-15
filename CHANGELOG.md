# Changelog

All notable changes to Funnel for Windows (codename `gnu-sorter-windows`) are
documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
with the same pre-release channels as the Linux app:
`indev` → `alpha` → `beta` → `rc` → stable.

## [Unreleased]

### Added
- **Green brand accent**: the app now wears Funnel's colour — the same lime as
  the Linux app and the website (`#a3e635` on dark, `#4d7c0f` on light, with
  `#0b0d08`/`#ffffff` text on top). WinUI bakes the system accent into a set of
  brushes, so the whole accent set (`SystemAccentColor`,
  `AccentFillColor*Brush`, `TextOnAccent*Brush`, selection indicators, …) is
  overridden per theme in `App.xaml`. High-contrast themes are left untouched.
- **Accent colour picker** in Settings: *Follow Windows*, *Funnel green* (the
  default) or any of the shared palette colours (lime, teal, cyan, blue,
  purple, pink, red, orange, yellow, slate). The choice is saved as `accent`
  in `settings.json` — the same key the Python app uses — and applied live via
  `AccentService` without restarting.
- **Rules page redesign**: a single, priority-ordered rule list (the old
  two-section Extensions/Names split hid the real first-match-wins order), with
  **drag-and-drop reordering**, a priority number per row, an inline enable
  switch and one `⋯` menu per rule (edit / duplicate / move up/down / remove).
  The toolbar is now **Add rule** (a split button whose dropdown holds the
  presets), a search box, and a single page `⋯` menu.
- **Explicit inheritance**: a folder using the default rules now shows
  *"Using the default rules"* with a **Customize…** button (instead of an
  ambiguous inherit toggle), and **Reset to defaults** reverts a customised
  folder from the page menu.
- **Overlap warnings**: rules that can never match — a duplicated extension, or
  anything below a catch-all — now raise an inline `InfoBar` and a per-row
  badge naming the rule and the reason.
- **Preview a folder** from the rules page: counts how many top-level files
  each rule would move before you sort, and the filename tester now reports
  which rule matched.
- **Rule import & export** (JSON) from the page menu, with *Replace* or
  *Append* on import; the file format matches the advanced editor and the Linux
  app.

### Changed
- **Top navigation tabs**: Folders · Rules · Activity now sit as tabs in the
  header (`NavigationView` in `Top` mode) instead of a left sidebar, mirroring
  the Linux shell, with Settings as the gear on the right.
- **Animated page changes**: the content frame uses a
  `NavigationThemeTransition`, so switching tabs animates.
- The header wordmark is tinted with the brand accent, matching Linux.
- **App icon is now green**, regenerated from `Assets/Icon.svg` (`Icon.png`, the
  multi-resolution `Icon.ico`, and the docs images).

### Fixed
- The saved theme is now applied on startup. It was previously applied before
  the window existed and silently did nothing, so a Light/Dark choice only took
  effect after the window was already shown.
- Rule summaries join matchers with **and** when a rule is set to match *all*
  conditions, instead of always using **or**.
- The folder *Enable* toggle now reflects the switch itself rather than the
  folder's stored state, so toggling a row works the first time.
- The rule wizard now validates the exclusion regex (like the match patterns),
  instead of accepting an invalid pattern and failing later.
- Size thresholds use 1024-based units (`1M` = 1,048,576 bytes), matching the
  Linux app and its `parse_size` (previously decimal, 1000-based).
- Size filters no longer match files whose size is unknown, matching Linux.
- The Rules page info bar no longer overlaps the rule list.
- Unhandled exceptions are now actually suppressed to keep the app alive, as
  the handler always intended (it never set `Handled`).

## [3.0.0] - 2026-10-12 "Tunneler"

### Added
- **Single-source version & branding**: product name, version, icon and
  description now live in `Directory.Build.props`; the About pane reads the
  real version at runtime instead of a hardcoded string.
- **Channel-aware updates**: the update check reads the release list and only
  ever offers a build whose channel matches this install
  (`indev`/`alpha`/`beta`/`rc`/`release`). A new **Check for updates
  automatically** switch lets you opt out of the startup check; the manual
  *Check for updates* button is unaffected.
- **Appearance**: a Theme choice (System default / Light / Dark) in Settings,
  applied via the window's requested theme.
- **Per-rule accent colour**: rules can carry a hex colour, shown as a dot on
  the rule row and edited in the rule dialog (mirrors the Linux `#rrggbb`).
- **Journal power tools**: the Activity page gains extension and size filters
  (`1M`, `500M`) alongside the existing name/rule search, plus an **On
  conflict** choice for restores — *Rename* (default, `name (1).ext`),
  *Overwrite* or *Skip*. The restore preview reports the new
  `overwrite`/`skip` outcomes.
- **Journal retention**: History settings let you cap the journal by entry
  count (default 2500) and by size in bytes (default 1,000,000); once it
  passes the byte ceiling it is trimmed to the newest entries.

### Changed
- Version scheme aligned with Linux (`X.Y.Z-dev.N` / `-alpha.N` / `-beta.N` /
  `-rc.N` / `X.Y.Z`), so the two platforms ship the same version on the same
  channel.

> The Windows app now mirrors Linux Phase 4 in full.
