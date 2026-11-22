#!/usr/bin/env bash
# Installs or updates zCast on a Raspberry Pi (64-bit Raspberry Pi OS with nginx).
# Run from the extracted package folder:
#   ./install.sh                  install / update
#   ./install.sh --set-password   change the admin page user/password
#   ./install.sh --new-cert       recreate the admin page HTTPS certificate (e.g. after an IP change)
# Environment overrides:
#   ZCAST_ALLOW="192.168.1.0/24 10.0.0.0/24"   networks allowed to use zCast (default: auto-detected LAN)
#   ZCAST_ADMIN_PORT=8090                       admin page port
set -euo pipefail
cd "$(dirname "$0")"

APP_DIR=/opt/zcast
DATA_FILE=/var/lib/zcast/stations.yml
NGINX=/etc/nginx
ALLOW_FILE=$NGINX/zcast-allow.conf
HTPASSWD=$NGINX/zcast-admin.htpasswd
CERT_DIR=$NGINX/zcast-admin
ADMIN_PORT=${ZCAST_ADMIN_PORT:-8090}

SET_PASSWORD=0
NEW_CERT=0
for arg in "$@"; do
    case "$arg" in
        --set-password) SET_PASSWORD=1 ;;
        --new-cert)     NEW_CERT=1 ;;
        -h|--help)      sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; exit 1 ;;
    esac
done

# ---------------------------------------------------------------- helpers
detect_lan() {
    local iface
    iface=$(ip -4 route show default 2>/dev/null | awk '{print $5; exit}')
    [ -n "$iface" ] && ip -o -4 route show dev "$iface" scope link 2>/dev/null | awk '{print $1; exit}'
}

write_allow_file() {
    local nets="$1" tmp
    tmp=$(mktemp)
    {
        echo "# Networks allowed to use zCast (AVR site and admin page). Edit, then:"
        echo "#   sudo nginx -t && sudo systemctl reload nginx"
        for net in $nets; do echo "allow $net;"; done
        echo "allow 127.0.0.1;"
        echo "deny  all;"
    } > "$tmp"
    sudo install -m 644 -o root -g root "$tmp" "$ALLOW_FILE"
    rm -f "$tmp"
    echo "    $ALLOW_FILE: $nets + 127.0.0.1"
}

ask_password() {
    if [ ! -t 0 ]; then
        echo "    No terminal: can't ask for the admin password. Run ./install.sh --set-password later." >&2
        return 1
    fi
    local user pass pass2 hash group tmp
    read -r -p "    Admin user name [admin]: " user
    user=${user:-admin}
    while true; do
        read -r -s -p "    Admin password (min. 8 characters): " pass; echo
        read -r -s -p "    Repeat password: " pass2; echo
        if [ "$pass" != "$pass2" ]; then echo "    Passwords don't match."; continue; fi
        if [ ${#pass} -lt 8 ]; then echo "    Too short."; continue; fi
        break
    done
    hash=$(printf '%s' "$pass" | openssl passwd -apr1 -stdin)
    group=root
    getent group www-data >/dev/null && group=www-data
    tmp=$(mktemp)
    printf '%s:%s\n' "$user" "$hash" > "$tmp"
    sudo install -m 640 -o root -g "$group" "$tmp" "$HTPASSWD"
    rm -f "$tmp"
    echo "    Password saved for user '$user' ($HTPASSWD)"
}

create_cert() {
    local ip
    ip=$(hostname -I | awk '{print $1}')
    sudo mkdir -p "$CERT_DIR"
    sudo openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=zcast-admin" \
        -addext "subjectAltName=IP:${ip},IP:127.0.0.1,DNS:$(hostname),DNS:localhost" \
        -keyout "$CERT_DIR/key.pem" -out "$CERT_DIR/cert.pem" 2>/dev/null
    sudo chmod 600 "$CERT_DIR/key.pem"
    echo "    Self-signed certificate for $ip created ($CERT_DIR, valid 10 years)"
}

# ---------------------------------------------------------------- password / certificate only
if [ "$SET_PASSWORD" = 1 ] || [ "$NEW_CERT" = 1 ]; then
    [ "$SET_PASSWORD" = 1 ] && { echo "==> Admin password"; ask_password; }
    [ "$NEW_CERT" = 1 ] && { echo "==> Admin certificate"; create_cert; }
    sudo nginx -t && sudo systemctl reload nginx
    echo "==> Done."
    exit 0
fi

# ---------------------------------------------------------------- app + service
echo "==> Stopping zCast (if running)"
sudo systemctl stop zcast 2>/dev/null || true

echo "==> Installing app to $APP_DIR"
sudo rm -rf "$APP_DIR.new"
sudo cp -r app "$APP_DIR.new"
# Versions before the admin page kept the list in /opt/zcast/stations.yml. Carry it over: zCast copies it
# to $DATA_FILE on first start (only if that file doesn't exist yet).
if [ -f "$APP_DIR/stations.yml" ] && ! sudo test -f "$DATA_FILE"; then
    echo "    keeping existing $APP_DIR/stations.yml (moves to $DATA_FILE on first start)"
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

# ---------------------------------------------------------------- nginx
echo "==> Network allowlist"
if [ -n "${ZCAST_ALLOW:-}" ]; then
    write_allow_file "$ZCAST_ALLOW"
elif [ ! -f "$ALLOW_FILE" ]; then
    LAN=$(detect_lan || true)
    if [ -z "$LAN" ]; then
        echo "    Could not detect the home network. Run again with e.g. ZCAST_ALLOW=\"192.168.1.0/24\" ./install.sh" >&2
        exit 1
    fi
    write_allow_file "$LAN"
else
    echo "    keeping $ALLOW_FILE: $(grep -E '^allow' "$ALLOW_FILE" | awk '{print $2}' | tr -d ';' | paste -sd ' ' -)"
fi

echo "==> AVR site (port 80)"
sudo cp nginx-zcast.conf "$NGINX/sites-available/zcast"
sudo ln -sf "$NGINX/sites-available/zcast" "$NGINX/sites-enabled/zcast"

echo "==> Admin site (port $ADMIN_PORT)"
ADMIN_ENABLED=1
[ -f "$CERT_DIR/cert.pem" ] || create_cert
if [ ! -f "$HTPASSWD" ]; then
    ask_password || ADMIN_ENABLED=0
fi
if [ "$ADMIN_ENABLED" = 1 ]; then
    if sudo ss -tlnp "sport = :$ADMIN_PORT" | grep -v nginx | grep -q LISTEN; then
        echo "    Port $ADMIN_PORT is used by another program. Run again with ZCAST_ADMIN_PORT=<free port>." >&2
        exit 1
    fi
    sed "s/8090/$ADMIN_PORT/g" nginx-zcast-admin.conf | sudo tee "$NGINX/sites-available/zcast-admin" >/dev/null
    sudo ln -sf "$NGINX/sites-available/zcast-admin" "$NGINX/sites-enabled/zcast-admin"
else
    sudo rm -f "$NGINX/sites-enabled/zcast-admin"
    echo "    Admin site not enabled (no password set)."
fi

sudo nginx -t
sudo systemctl reload nginx

# ---------------------------------------------------------------- tests
echo "==> Testing"
for i in $(seq 1 15); do
    curl -fs -o /dev/null http://127.0.0.1:8010/ycast && break
    sleep 1
done
echo -n "    AVR login via nginx:  "
curl -s -H "Host: radioyamaha.vtuner.com" \
    "http://127.0.0.1/setupapp/Yamaha/asp/BrowseXML/loginXML.asp?token=0"
echo
echo -n "    Main menu items:      "
curl -s -H "Host: radioyamaha.vtuner.com" "http://127.0.0.1/ycast" | grep -o '<Title>[^<]*' | sed 's/<Title>//' | paste -sd ',' -
echo -n "    Admin hidden on AVR:  "
curl -s -o /dev/null -w '%{http_code} (expect 404)\n' -H "Host: radioyamaha.vtuner.com" "http://127.0.0.1/admin/"
if [ "$ADMIN_ENABLED" = 1 ]; then
    echo -n "    Admin page:           "
    curl -sk -o /dev/null -w '%{http_code} (expect 401 = password required)\n' "https://127.0.0.1:$ADMIN_PORT/admin/"
    echo "==> Admin page: https://$(hostname -I | awk '{print $1}'):$ADMIN_PORT/  (accept the certificate warning once)"
fi
echo "==> Done. Logs: journalctl -u zcast -f"
