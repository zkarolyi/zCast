# zCast

[![Build](https://github.com/zkarolyi/zCast/actions/workflows/build.yml/badge.svg)](https://github.com/zkarolyi/zCast/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

Self-hosted replacement for the **vTuner** internet radio service used by many network AV receivers
(Yamaha, Denon, Marantz, Onkyo, …). A C# / .NET 10 port of [YCast](https://github.com/milaq/YCast) 1.1.0.

Many AVRs built between roughly 2011 and 2017 get their internet radio menu from vTuner, which is now a paid
or unreliable service. zCast answers those requests on your own network, so the receiver's **Net Radio** works again.

## Features

- **Radiobrowser**: browse stations by genre, country, language, or the most popular ones, using the free
  [Community Radio Browser](https://www.radio-browser.info) index
- **My Stations**: your own categorized station list in a simple YAML file
- **Admin page** to edit My Stations from a browser on the home network (search, stream test, backups)
- **Search** from the receiver's remote
- **Station logos**, resized and converted to JPEG for the receiver
- Runs on Windows, Linux and **Raspberry Pi** (self-contained ARM64 build, no .NET install needed)
- Small footprint: runs comfortably next to other services on a Raspberry Pi

## How it works

```
AVR ─────DNS: radioyamaha.vtuner.com → server──▶ nginx :80   ──▶ zCast 127.0.0.1:8010 ──▶ Radio Browser / streams
Browser (LAN) ───https://<server-ip>:8090/──────▶ nginx :8090 ──▶ zCast 127.0.0.1:8011 (admin page)
```

1. Your local DNS answers the vTuner host name your receiver uses with the IP of the zCast server.
2. A reverse proxy (nginx) on port 80 forwards those requests to zCast.
3. zCast responds in the vTuner XML format, and the receiver plays the streams directly.
4. The admin page has its own nginx site: home network only, password, HTTPS.

## Supported devices

zCast implements the same protocol as YCast, so it should work with the
[AVRs that YCast supports](https://github.com/milaq/YCast#supported-devices).
The default deployment targets **Yamaha** (`radioyamaha.vtuner.com`, `radioyamaha2.vtuner.com`).
Other brands use different vTuner host names. See the YCast README and add them to `deploy/nginx-zcast.conf`.

Tested a device? Please [open an issue](https://github.com/zkarolyi/zCast/issues/new/choose) so it can be listed here.

## Installation (Raspberry Pi, 64-bit)

Requirements: a 64-bit Raspberry Pi OS (`uname -m` shows `aarch64`) and nginx listening on port 80
(if it is not installed yet: `sudo apt install nginx`).

**1. Build the package on a PC** with the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publish.ps1
scp out\zcast-linux-arm64.tar.gz <user>@<pi-ip>:~
```

Or download `zcast-linux-arm64.tar.gz` from the latest [GitHub Actions build](https://github.com/zkarolyi/zCast/actions/workflows/build.yml).

**2. Install it on the Pi:**

```bash
tar xzf zcast-linux-arm64.tar.gz
cd zcast-linux-arm64
chmod +x install.sh && ./install.sh
```

On the first run it asks for the admin page user and password. It installs:

| What | Where |
|---|---|
| App | `/opt/zcast` |
| Service `zcast` | `127.0.0.1:8010` (AVR), `127.0.0.1:8011` (admin) |
| My Stations data | `/var/lib/zcast/stations.yml` (+ `backups/`) |
| nginx AVR site | `/etc/nginx/sites-available/zcast` (port 80, vTuner host names) |
| nginx admin site | `/etc/nginx/sites-available/zcast-admin` (port 8090, HTTPS) |
| Allowed networks | `/etc/nginx/zcast-allow.conf` (auto-detected LAN) |
| Admin password | `/etc/nginx/zcast-admin.htpasswd` |
| Admin certificate | `/etc/nginx/zcast-admin/` (self-signed, 10 years) |

Running it again updates zCast and keeps the data, allowlist, password and certificate.
Options: `--set-password`, `--new-cert`; environment: `ZCAST_ALLOW="192.168.1.0/24"`, `ZCAST_ADMIN_PORT=8090`.

**3. Redirect DNS.** Make `radioyamaha.vtuner.com` and `radioyamaha2.vtuner.com` resolve to the Pi's IP.
Use whichever of these your network supports:

- **Router**: a local hosts / static DNS feature (for example *DNS Service → Hosts* on ZTE routers)
- **Pi-hole / dnsmasq**: `address=/radioyamaha.vtuner.com/<pi-ip>`
- **Synology DNS Server** or any other local DNS server

If the receiver doesn't use your router for DNS, set its primary DNS manually.
Give the server a fixed IP address. Check from a PC:

```
nslookup radioyamaha.vtuner.com
```

**4.** Power-cycle the receiver and open **Net Radio**.

## Configuration: My Stations

The active list is `/var/lib/zcast/stations.yml` on the server. It is read on every request, so no restart is needed.
`web/stations.yml` in the repo is only the initial copy, used when the data file doesn't exist yet.

**Edit in the browser** (recommended): see [Admin page](#admin-page).

**Edit on the server:**

```bash
sudo nano /var/lib/zcast/stations.yml
```

**Or edit locally and copy:**

```bash
scp web/stations.yml <user>@<pi-ip>:~
ssh <user>@<pi-ip> "sudo cp ~/stations.yml /var/lib/zcast/stations.yml"
```

**Format:**

```yaml
Categories:            # each category is a folder under "My Stations"
  Chillout:
    - Name: SomaFM Drone Zone
      Url: http://ice1.somafm.com/dronezone-256-mp3
  Hungarian:
    - Name: "Petőfi Rádió"   # quote names with ':', '#' or non-ASCII characters
      Url: http://icast.connectmedia.hu/4738/mr2.mp3
```

- Indent with spaces, not tabs.
- `Url` must be a direct stream (MP3/AAC/Icecast/Shoutcast), not a web page. Prefer `http`: older AVRs often can't play `https`.
- The station ID is derived from `Name` + `Url`. Changing either invalidates presets saved on the AVR.

**Verify:**

```bash
curl -s -H "Host: radioyamaha.vtuner.com" http://127.0.0.1/ycast/my_stations | grep -o '<Title>[^<]*'
journalctl -u zcast -n 20 | grep -i "station configuration"   # YAML errors (My Stations shows "No entries found")
```

If the AVR still shows the old list, re-open the menu or power-cycle it (menus are cached).

## Admin page

`https://<server-ip>:8090/` from a device on the home network. Accept the self-signed certificate warning once,
then log in with the user and password set by `install.sh`.

- Categories and stations: add, edit, delete, reorder, move between categories
- **Search** the Radio Browser index and add a station with one click (direct stream URL)
- **Test** a stream URL (HTTP status, content type, HTTPS/playlist/web page warnings)
- Import / export `stations.yml`; the last 10 versions are kept as backups and can be restored
- Saving is atomic and conflict-safe (two open tabs can't overwrite each other)
- Saving from the page rewrites the file: YAML comments are not kept (backups keep the previous version)

**Access control** (all three apply):

| Layer | Setting |
|---|---|
| Network | `/etc/nginx/zcast-allow.conf`, also applied to the AVR site |
| Password | nginx basic auth, `/etc/nginx/zcast-admin.htpasswd` |
| Encryption | HTTPS on port 8090 (self-signed) |

```bash
sudo nano /etc/nginx/zcast-allow.conf && sudo nginx -t && sudo systemctl reload nginx   # other network
./install.sh --set-password                                                                # change password
./install.sh --new-cert                                                                    # server IP changed
```

```nginx
# /etc/nginx/zcast-allow.conf
allow 192.168.1.0/24;
allow 127.0.0.1;
deny  all;
```

Never forward port 8090 on the router. zCast itself only listens on `127.0.0.1`; the admin API answers only on the
admin port and rejects cross-site requests.

## Operation

| Task | Command (on the Pi) |
|---|---|
| Service logs | `journalctl -u zcast -f` |
| Requests from the receiver | `sudo tail -f /var/log/nginx/zcast.access.log` |
| Admin page requests | `sudo tail -f /var/log/nginx/zcast-admin.access.log` |
| My Stations backups | `sudo ls /var/lib/zcast/backups` |
| Restart | `sudo systemctl restart zcast` |
| Test | `curl -H "Host: radioyamaha.vtuner.com" http://127.0.0.1/ycast` |

## Development

```bash
dotnet run --project web        # AVR API: http://localhost:5001/swagger   admin page: http://localhost:5002/admin/
dotnet test                     # unit tests
```

Configuration (environment variables): `Admin__Port` (admin port; unset = admin page disabled),
`Stations__File` (path of `stations.yml`; default: next to the binaries).
In development the admin page has no password (no nginx in front).

Useful endpoints: `/ycast` (main menu), `/ycast/radiobrowser`, `/ycast/my_stations`,
`/ycast/search?search=jazz`, `/setupapp/Yamaha/asp/BrowseXML/loginXML.asp?token=0` (AVR login).

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to contribute and [CHANGELOG.md](CHANGELOG.md) for release notes.

## Credits

- [YCast](https://github.com/milaq/YCast) by Micha LaQua: the original Python implementation zCast is ported from
- [Community Radio Browser](https://www.radio-browser.info): the free station index

zCast is not affiliated with vTuner, Yamaha or any other manufacturer.

## License

zCast is free software, licensed under the [GNU General Public License v3.0 or later](LICENSE),
the same license as YCast. See [NOTICE](NOTICE) for copyright and third-party information.
