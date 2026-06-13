#!/usr/bin/env bash
#
# AxoPrint relay installer — run this ON your server, from the repo root.
# Supports Debian/Ubuntu (apt) and AlmaLinux/RHEL/Rocky/Fedora (dnf).
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
#   5. Opens the firewall, handles SELinux, starts everything, prints URL + token.
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

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RELAY_PROJECT="$REPO_ROOT/src/AxoPrint.Relay"

if [[ ! -d "$RELAY_PROJECT" ]]; then
    echo "Cannot find $RELAY_PROJECT — run this from the cloned repo." >&2
    exit 1
fi

if [[ -z "$TOKEN" ]]; then
    TOKEN="$(openssl rand -hex 32 2>/dev/null || head -c 32 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 48)"
    GENERATED_TOKEN=1
else
    GENERATED_TOKEN=0
fi

log() { echo -e "\n\033[1;34m==>\033[0m \033[1m$*\033[0m"; }

# ----------------------------------------------------- detect package family
RPM=""
if command -v apt-get >/dev/null 2>&1; then
    FAMILY="debian"
elif command -v dnf >/dev/null 2>&1; then
    FAMILY="rhel"; RPM="dnf"
elif command -v yum >/dev/null 2>&1; then
    FAMILY="rhel"; RPM="yum"
else
    echo "Unsupported distro: need apt-get or dnf/yum." >&2
    exit 1
fi
echo "Detected package family: $FAMILY"

# ---------------------------------------------------------- 1. dependencies
log "Installing base packages"
if [[ "$FAMILY" == "debian" ]]; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq curl ca-certificates gnupg apt-transport-https libicu-dev openssl >/dev/null
else
    "$RPM" install -y -q curl ca-certificates gnupg2 libicu openssl >/dev/null
fi

# ------------------------------------------------------------- 2. .NET SDK
DOTNET=""
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    DOTNET="$(command -v dotnet)"
elif [[ -x "$DOTNET_DIR/dotnet" ]] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
    DOTNET="$DOTNET_DIR/dotnet"
else
    log "Installing .NET 10 SDK into $DOTNET_DIR"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
    DOTNET="$DOTNET_DIR/dotnet"
fi
echo "Using dotnet: $DOTNET ($("$DOTNET" --version))"

# --------------------------------------------------------------- 3. build
log "Building the relay (self-contained linux-x64) → $APP_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
rm -rf "$APP_DIR"
"$DOTNET" publish "$RELAY_PROJECT" -c Release -r linux-x64 --self-contained -o "$APP_DIR"
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

# SELinux (AlmaLinux/RHEL): give the relay binary the right exec context.
if command -v restorecon >/dev/null 2>&1; then
    restorecon -RF "$APP_DIR" 2>/dev/null || true
fi

systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null 2>&1 || true
# restart (not just start) so a rewritten token/env is always applied.
systemctl restart "$SERVICE"

# --------------------------------------------------------------- 5. Caddy
if ! command -v caddy >/dev/null 2>&1; then
    log "Installing Caddy"
    if [[ "$FAMILY" == "debian" ]]; then
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
            | gpg --batch --yes --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
            > /etc/apt/sources.list.d/caddy-stable.list
        apt-get update -qq
        apt-get install -y -qq caddy >/dev/null
    else
        # Caddy's official RPM repo (works on AlmaLinux/RHEL/Rocky/Fedora).
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/setup.rpm.sh' | bash
        "$RPM" install -y -q caddy >/dev/null
    fi
fi

log "Configuring Caddy for https://$DOMAIN"
CADDYFILE="/etc/caddy/Caddyfile"
mkdir -p /etc/caddy
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

# SELinux: allow Caddy (and confined services) to make the local proxy connection.
if command -v getenforce >/dev/null 2>&1 && [[ "$(getenforce)" == "Enforcing" ]]; then
    log "SELinux enforcing — allowing outbound HTTP proxy connections"
    setsebool -P httpd_can_network_connect 1 2>/dev/null || true
fi

systemctl enable caddy >/dev/null 2>&1 || true
systemctl reload caddy 2>/dev/null || systemctl restart caddy

# ------------------------------------------------------------- 6. firewall
if command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld; then
    log "Opening ports 80/443 in firewalld"
    firewall-cmd --permanent --add-service=http  >/dev/null || true
    firewall-cmd --permanent --add-service=https >/dev/null || true
    firewall-cmd --reload >/dev/null || true
elif command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    log "Opening ports 80/443 in ufw"
    ufw allow 80/tcp  >/dev/null || true
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
