#!/usr/bin/env bash
#
# AxoPrint relay installer — run this ON your Debian server, from the repo root.
#
#   git clone https://github.com/bigtaed-sys/axoprint.git
#   cd axoprint
#   sudo bash deploy/install.sh print.example.com [token]
#
# What it does:
#   1. Installs the .NET 10 SDK (only if missing) into /opt/dotnet.
#   2. Builds the relay as a self-contained binary into /opt/axoprint.
#   3. Creates a systemd service (axoprint-relay) listening on 127.0.0.1.
#   4. Installs Caddy (if missing) and configures it as a TLS reverse proxy
#      for your domain (automatic Let's Encrypt certificate).
#   5. Enables and starts everything, then prints the URL and token.
#
# Prerequisites:
#   - A domain (DNS A/AAAA record) pointing at this server.
#   - Ports 80 and 443 reachable from the internet.
#
set -euo pipefail

# ---------------------------------------------------------------- arguments
DOMAIN="${1:-}"
TOKEN="${2:-${AXO_TOKEN:-}}"
PORT="${AXO_PORT:-5080}"
DATA_DIR="${AXO_DATADIR:-/var/lib/axoprint}"
APP_DIR="${AXO_APPDIR:-/opt/axoprint}"
DOTNET_DIR="/opt/dotnet"
SERVICE="axoprint-relay"

if [[ -z "$DOMAIN" ]]; then
    echo "Usage: sudo bash deploy/install.sh <domain> [token]" >&2
    echo "  e.g. sudo bash deploy/install.sh print.example.com" >&2
    exit 1
fi

# Re-exec with sudo if not root.
if [[ "$(id -u)" -ne 0 ]]; then
    exec sudo -E bash "$0" "$@"
fi

# Repo root = parent of this script's directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RELAY_PROJECT="$REPO_ROOT/src/AxoPrint.Relay"

if [[ ! -d "$RELAY_PROJECT" ]]; then
    echo "Cannot find $RELAY_PROJECT — run this from the cloned repo." >&2
    exit 1
fi

# Generate a token if none was supplied.
if [[ -z "$TOKEN" ]]; then
    TOKEN="$(openssl rand -hex 32 2>/dev/null || head -c 32 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 48)"
    GENERATED_TOKEN=1
else
    GENERATED_TOKEN=0
fi

log() { echo -e "\n\033[1;34m==>\033[0m \033[1m$*\033[0m"; }

# ---------------------------------------------------------- 1. dependencies
log "Installing base packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq curl ca-certificates gnupg apt-transport-https libicu-dev openssl >/dev/null

# ------------------------------------------------------------- 2. .NET SDK
need_dotnet=1
if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then need_dotnet=0; fi
fi
if [[ -x "$DOTNET_DIR/dotnet" ]] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
    DOTNET="$DOTNET_DIR/dotnet"; need_dotnet=0
fi

if [[ "$need_dotnet" -eq 1 ]]; then
    log "Installing .NET 10 SDK into $DOTNET_DIR"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
    DOTNET="$DOTNET_DIR/dotnet"
else
    DOTNET="${DOTNET:-$(command -v dotnet || echo "$DOTNET_DIR/dotnet")}"
fi
echo "Using dotnet: $DOTNET ($("$DOTNET" --version))"

# --------------------------------------------------------------- 3. build
log "Building the relay (self-contained linux-x64) → $APP_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
rm -rf "$APP_DIR"
"$DOTNET" publish "$RELAY_PROJECT" -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=false -o "$APP_DIR"
chmod +x "$APP_DIR/AxoPrint.Relay"

# ----------------------------------------------------------- 4. systemd unit
log "Creating systemd service: $SERVICE"
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=AxoPrint relay (remote print service)
After=network.target

[Service]
Type=notify
WorkingDirectory=$APP_DIR
ExecStart=$APP_DIR/AxoPrint.Relay
Restart=always
RestartSec=5

Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT
Environment=DOTNET_ENVIRONMENT=Production
Environment=AXO__TOKEN=$TOKEN
Environment=AXO__BASEURI=https://$DOMAIN
Environment=AXO__DATADIR=$DATA_DIR

# Hardening. StateDirectory creates $DATA_DIR (under /var/lib) owned by the
# dynamic user and makes it writable even with ProtectSystem=strict.
DynamicUser=true
StateDirectory=$(basename "$DATA_DIR")
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now "$SERVICE"

# --------------------------------------------------------------- 5. Caddy
if ! command -v caddy >/dev/null 2>&1; then
    log "Installing Caddy"
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
        | gpg --batch --yes --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
        > /etc/apt/sources.list.d/caddy-stable.list
    apt-get update -qq
    apt-get install -y -qq caddy >/dev/null
fi

log "Configuring Caddy for https://$DOMAIN"
CADDYFILE="/etc/caddy/Caddyfile"
if [[ -f "$CADDYFILE" ]] && ! grep -q "# axoprint:$DOMAIN" "$CADDYFILE"; then
    cp "$CADDYFILE" "$CADDYFILE.bak.$(date +%s)"
    echo "  (backed up existing Caddyfile)"
fi
cat > "$CADDYFILE" <<CADDY
# axoprint:$DOMAIN
$DOMAIN {
	reverse_proxy 127.0.0.1:$PORT
	request_body {
		max_size 512MB
	}
	encode zstd gzip
}
CADDY
systemctl reload caddy || systemctl restart caddy

# ------------------------------------------------------------- 6. firewall
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
    log "Opening ports 80/443 in ufw"
    ufw allow 80/tcp >/dev/null || true
    ufw allow 443/tcp >/dev/null || true
fi

# ---------------------------------------------------------------- summary
sleep 2
log "Done"
echo "Relay service:  $(systemctl is-active "$SERVICE")"
echo "Caddy service:  $(systemctl is-active caddy)"
echo
echo "  URL:    https://$DOMAIN"
echo "  Token:  $TOKEN"
if [[ "$GENERATED_TOKEN" -eq 1 ]]; then
    echo "          (generated — use this same token in the Agent and Setup apps)"
fi
echo
echo "Check it: curl -s https://$DOMAIN/   (allow a minute for the TLS certificate)"
echo "Logs:     journalctl -u $SERVICE -f"
