#!/usr/bin/env bash
# Installs or updates zCast on a Raspberry Pi (64-bit Raspberry Pi OS with nginx).
# Run from the extracted package folder:  ./install.sh
set -euo pipefail
cd "$(dirname "$0")"

APP_DIR=/opt/zcast
SITE=/etc/nginx/sites-available/zcast

echo "==> Stopping zCast (if running)"
sudo systemctl stop zcast 2>/dev/null || true

echo "==> Installing app to $APP_DIR"
sudo rm -rf "$APP_DIR.new"
sudo cp -r app "$APP_DIR.new"
# Keep the stations.yml already on the Pi when updating.
if [ -f "$APP_DIR/stations.yml" ]; then
    echo "    keeping existing $APP_DIR/stations.yml"
    sudo cp "$APP_DIR/stations.yml" "$APP_DIR.new/stations.yml"
fi
sudo rm -rf "$APP_DIR"
sudo mv "$APP_DIR.new" "$APP_DIR"
sudo chown -R root:root "$APP_DIR"
sudo chmod -R a+rX "$APP_DIR"
sudo chmod 755 "$APP_DIR/web"

echo "==> Installing systemd service"
sudo cp zcast.service /etc/systemd/system/zcast.service
sudo systemctl daemon-reload
sudo systemctl enable --now zcast

echo "==> Installing nginx site"
sudo cp nginx-zcast.conf "$SITE"
sudo ln -sf "$SITE" /etc/nginx/sites-enabled/zcast
sudo nginx -t
sudo systemctl reload nginx

echo "==> Testing"
for i in $(seq 1 15); do
    curl -fs -o /dev/null http://127.0.0.1:8010/ycast && break
    sleep 1
done
echo -n "    AVR login via nginx: "
curl -s -H "Host: radioyamaha.vtuner.com" \
    "http://127.0.0.1/setupapp/Yamaha/asp/BrowseXML/loginXML.asp?token=0"
echo
echo -n "    Main menu items:     "
curl -s -H "Host: radioyamaha.vtuner.com" "http://127.0.0.1/ycast" | grep -o '<Title>[^<]*' | sed 's/<Title>//' | paste -sd ',' -
echo "==> Done. Logs: journalctl -u zcast -f"
