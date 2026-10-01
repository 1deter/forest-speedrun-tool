#!/bin/sh
# One-time setup of forest.deter.cloud on the VPS. Run from a checkout of
# site/deploy (or the three files copied over):
#
#   sudo sh setup.sh ~/website/Caddyfile
#
# Safe to run again: it keeps the existing data, admin token and key.
set -eu
CADDYFILE="${1:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"
DOCKER="$(command -v docker)"
[ "$(id -u)" = 0 ] || { echo "run with sudo" >&2; exit 1; }
[ "$DOCKER" = /usr/bin/docker ] || { echo "docker is at $DOCKER, expected /usr/bin/docker (edit deploy.sh and the sudoers line)" >&2; exit 1; }

# The deploy user: no password, no shell use beyond deploy.sh.
id forestdeploy >/dev/null 2>&1 || useradd --system --create-home --shell /bin/sh forestdeploy

mkdir -p /opt/forest-site/app/releases /var/lib/forest-site
install -m 755 -o root -g root "$HERE/deploy.sh" /opt/forest-site/deploy.sh
install -m 644 -o root -g root "$HERE/compose.yaml" /opt/forest-site/compose.yaml
chown -R forestdeploy:forestdeploy /opt/forest-site/app
# The container runs as the image's `app` user (uid 1654, compose.yaml):
# the data is its, and nobody else's on the host.
chown -R 1654:1654 /var/lib/forest-site
chmod 700 /var/lib/forest-site

if [ ! -f /opt/forest-site/.env ]; then
  umask 077
  echo "FOREST_ADMIN_TOKEN=$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')" > /opt/forest-site/.env
  umask 022
fi

echo "forestdeploy ALL=(root) NOPASSWD: /usr/bin/docker restart forest-site" > /etc/sudoers.d/forest-site
chmod 440 /etc/sudoers.d/forest-site
visudo -cf /etc/sudoers.d/forest-site >/dev/null

# The deploy key: made here, public half locked to deploy.sh, private half
# printed once for GitHub and then deleted from this machine.
SSHDIR=/home/forestdeploy/.ssh
mkdir -p "$SSHDIR"
NEWKEY=""
if [ ! -f "$SSHDIR/authorized_keys" ]; then
  TMP="$(mktemp -d)"
  ssh-keygen -q -t ed25519 -N "" -C "github-actions forest-site" -f "$TMP/key"
  echo "command=\"/opt/forest-site/deploy.sh\",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding $(cat "$TMP/key.pub")" > "$SSHDIR/authorized_keys"
  NEWKEY="$(cat "$TMP/key")"
  rm -rf "$TMP"
fi
chown -R forestdeploy:forestdeploy "$SSHDIR"
chmod 700 "$SSHDIR"; chmod 600 "$SSHDIR/authorized_keys"

# The container: created, and started by the first deploy. On a re-run
# with a release already there, recreated with the new compose.yaml and
# started (`docker restart` in deploy.sh never applies compose changes).
cd /opt/forest-site
if [ -e /opt/forest-site/app/current ]; then "$DOCKER" compose up -d --force-recreate && echo "forest-site recreated and started."
else "$DOCKER" compose up --no-start; fi

# Caddy: one more site, then a reload.
if [ -n "$CADDYFILE" ]; then
  if grep -q "forest.deter.cloud" "$CADDYFILE"; then
    echo "Caddyfile already has forest.deter.cloud - left as it is."
  else
    cp "$CADDYFILE" "$CADDYFILE.before-forest"
    printf '\nforest.deter.cloud {\n\treverse_proxy forest-site:8080\n}\n' >> "$CADDYFILE"
    CADDY="$("$DOCKER" ps --format '{{.Names}}' | grep -m1 caddy || true)"
    if [ -n "$CADDY" ]; then "$DOCKER" exec "$CADDY" caddy reload --config /etc/caddy/Caddyfile && echo "Caddy reloaded ($CADDY)."
    else echo "No running caddy container found - reload Caddy yourself."; fi
  fi
fi

echo
echo "================ for GitHub: repo Settings -> Secrets and variables -> Actions ================"
echo "DEPLOY_HOST      = this server's public IP address"
echo "DEPLOY_HOST_KEY  = the line below:"
cat /etc/ssh/ssh_host_ed25519_key.pub
if [ -n "$NEWKEY" ]; then
  echo "DEPLOY_KEY       = everything below, BEGIN and END lines included (shown once, not kept here):"
  echo "$NEWKEY"
else
  echo "DEPLOY_KEY       = unchanged (the key was made on an earlier run; delete $SSHDIR/authorized_keys and re-run for a new one)"
fi
echo "==========================================================================================="
echo "Admin token (for /api/admin, keep it private): sudo cat /opt/forest-site/.env"
