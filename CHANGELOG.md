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

### Changed
- Version scheme aligned with Linux (`X.Y.Z-dev.N` / `-alpha.N` / `-beta.N` /
  `-rc.N` / `X.Y.Z`). The current build is `3.0.0-dev.0` (indev).

> Entries for the journal and customization work land here as the Windows
> mirror of Linux Phase 4 proceeds.
