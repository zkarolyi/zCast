# Contributing to zCast

Thanks for your interest in improving zCast! Bug reports, device reports and pull requests are all welcome.

## Reporting a device

The most useful contribution is telling us whether zCast works with your receiver.
[Open a device report](https://github.com/zkarolyi/zCast/issues/new/choose) with the model and what worked.

## Reporting bugs

[Open a bug report](https://github.com/zkarolyi/zCast/issues/new/choose) and include:

- receiver model, and where zCast runs (Raspberry Pi model / OS, Windows, …)
- what you did, what you expected and what happened
- relevant log lines: `journalctl -u zcast -n 100` and `/var/log/nginx/zcast.access.log`

Please report security problems privately. See [SECURITY.md](SECURITY.md).

## Development setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Clone and run:
   ```bash
   git clone https://github.com/zkarolyi/zCast.git
   cd zCast
   dotnet run --project web
   ```
3. Open http://localhost:5001/swagger, or call the endpoints the receiver uses:
   ```bash
   curl "http://localhost:5001/setupapp/Yamaha/asp/BrowseXML/loginXML.asp?token=0"
   curl "http://localhost:5001/ycast"
   ```

## Project layout

| Path | Contents |
|---|---|
| `web/Controllers/YCastController.cs` | All HTTP endpoints (vTuner / YCast routes) |
| `web/Components/VTuner/` | vTuner XML page model |
| `web/Components/RadioBrowser/` | Radio Browser API client |
| `web/Components/MyStations/` | `stations.yml` reader |
| `web/Helpers/` | Shared helpers, station icon conversion |
| `deploy/` | Raspberry Pi package, installer, systemd and nginx configuration |

The code mirrors the structure of [YCast](https://github.com/milaq/YCast). The original Python is kept as comments
next to each ported function, which makes it easy to compare behavior.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep changes focused: one fix or feature per pull request.
3. Make sure `dotnet build` passes without new warnings.
4. Test against the receiver endpoints (at least `/setupapp/.../loginXML.asp` and `/ycast`), ideally on a real AVR.
5. Add a line under **[Unreleased]** in [CHANGELOG.md](CHANGELOG.md).
6. Describe what you changed and how you tested it in the pull request.

### Style

- Follow the existing code style. Files use CRLF line endings, except the Linux files in `deploy/`, which must use LF
  (enforced by `.gitattributes`).
- Keep the vTuner XML output compatible: receivers are strict and can't be updated.

## License

By contributing, you agree that your contributions are licensed under the
[GNU General Public License v3.0 or later](LICENSE), the license of this project.

## Code of conduct

This project follows the [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold it.
