# zCast

Self-hosted replacement for the vTuner internet radio service used by AV receivers (Yamaha, Denon, Marantz, …).
C# / .NET 10 port of [YCast](https://github.com/milaq/YCast) 1.1.0.

The receiver gets two menus:
- **Radiobrowser** – genres, countries, languages and most popular stations from the [Community Radio Browser](https://www.radio-browser.info)
- **My Stations** – your own stations from `web/stations.yml`

## How it works

```
Yamaha AVR ──DNS: radioyamaha.vtuner.com → Pi──▶ nginx :80 ──▶ zCast 127.0.0.1:8010 ──▶ Radio Browser / streams
```

1. The router's DNS answers `radioyamaha.vtuner.com` and `radioyamaha2.vtuner.com` with the Raspberry Pi's IP.
2. On the Pi, nginx forwards those host names to zCast.
3. zCast answers like the old vTuner service.

## Deploy to the Raspberry Pi (64-bit)

**On the PC** – build the package (self-contained, no .NET install needed on the Pi):

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publish.ps1
scp out\zcast-linux-arm64.tar.gz <user>@<pi-ip>:~
```

**On the Pi:**

```bash
tar xzf zcast-linux-arm64.tar.gz
cd zcast-linux-arm64
chmod +x install.sh && ./install.sh
```

The script installs the app to `/opt/zcast`, the `zcast` systemd service and the nginx site, then tests it.
Updating works the same way. An existing `/opt/zcast/stations.yml` is kept.

**On the router (ZTE F668)** – *DNS Service → Hosts*: add `radioyamaha.vtuner.com` and `radioyamaha2.vtuner.com`
pointing to the Pi's IP. Give the Pi a fixed IP (DHCP binding). Check from the PC:

```
nslookup radioyamaha.vtuner.com
```

If this doesn't return the Pi's IP, set the receiver's primary DNS manually to the router's IP.

## Operation

| Task | Command (on the Pi) |
|---|---|
| Logs | `journalctl -u zcast -f` |
| Restart | `sudo systemctl restart zcast` |
| Edit own stations | `sudo nano /opt/zcast/stations.yml` (read on every request, no restart needed) |
| Test | `curl -H "Host: radioyamaha.vtuner.com" http://127.0.0.1/ycast` |
| Swagger (on the Pi only) | `curl http://127.0.0.1:8010/swagger/v1/swagger.json` |

## stations.yml format

```yaml
Categories:
  Chillout:
    - Name: SomaFM Drone Zone
      Url: http://ice1.somafm.com/dronezone-256-mp3
```

## Development

```
dotnet run --project web      # http://localhost:5001/swagger
```
