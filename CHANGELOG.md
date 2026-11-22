# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Admin page for My Stations (`https://<server-ip>:8090/`): edit categories and stations, Radio Browser search,
  stream test, import/export, backups with restore
- Admin access control: own nginx site with LAN allowlist, basic auth and self-signed HTTPS; admin API only on its
  own port, cross-site request protection, security headers
- `install.sh`: LAN allowlist detection (`ZCAST_ALLOW`), admin password (`--set-password`), certificate (`--new-cert`),
  admin port (`ZCAST_ADMIN_PORT`)
- Unit tests for the station storage, run in CI

### Changed
- My Stations data moved to `/var/lib/zcast/stations.yml` (writable state directory); an existing
  `/opt/zcast/stations.yml` is migrated automatically. Saves are atomic and keep the last 10 versions.
- The AVR site (port 80) only accepts the home network
- Configuration: `Admin__Port`, `Stations__File`

## [1.0.0] - 2026-11-21

First public release. A C# / .NET port of [YCast](https://github.com/milaq/YCast) 1.1.0.

### Added
- vTuner-compatible API: main menu, Radiobrowser (genres, countries, languages, most popular),
  My Stations, search, station info, stream redirect and station icons
- `/setupapp/...` endpoints used by AVRs at login (Yamaha `loginXML.asp`, `navXML.asp`, `FavXML.asp`, `statxml.asp`, search)
- Raspberry Pi deployment: self-contained `linux-arm64` package, `install.sh`, systemd service and nginx site
- Cross-platform station icon conversion with SkiaSharp
- GitHub Actions build that produces the Raspberry Pi package

### Fixed (compared to the initial port)
- AVR login route now matches `/setupapp/...` at the server root with multiple path segments
- Search through `/setupapp` and the `search` query parameter
- My Stations and station info returned JSON instead of vTuner XML through `/setupapp`
- My Stations lookup by station id
- Unknown Radio Browser stations no longer crash the server (404 instead of 500)
- Radio Browser responses with `[` in station names, and URL-encoding of query values
- Original stream URL is kept when the playable URL can't be resolved
- Failed icon downloads are no longer cached

### Changed
- Target framework .NET 6 → .NET 10; dependencies updated, vulnerable packages removed

[Unreleased]: https://github.com/zkarolyi/zCast/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/zkarolyi/zCast/releases/tag/v1.0.0
