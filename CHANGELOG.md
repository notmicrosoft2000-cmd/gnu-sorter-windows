# Changelog

All notable changes to Funnel for Windows (codename `gnu-sorter-windows`) are
documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
with the same pre-release channels as the Linux app:
`indev` → `alpha` → `beta` → `rc` → stable.

## [Unreleased] — 3.0.0 "Tunneler"

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
  `-rc.N` / `X.Y.Z`). The current build is `3.0.0-dev.0` (indev).

> The Windows app now mirrors Linux Phase 4 in full; the next milestone is the
> `3.0.0` release.
