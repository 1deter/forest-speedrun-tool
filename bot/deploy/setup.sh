#!/bin/sh
# One-time setup of the knowledge bot on the VPS. Run from a checkout of
# bot/deploy (or the three files copied over):
#
#   sudo sh setup.sh
#
# Safe to run again: it keeps the data, the .env and the key.
set -eu
HERE="$(cd "$(dirname "$0")" && pwd)"
DOCKER="$(command -v docker)"
[ "$(id -u)" = 0 ] || { echo "run with sudo" >&2; exit 1; }
[ "$DOCKER" = /usr/bin/docker ] || { echo "docker is at $DOCKER, expected /usr/bin/docker (edit deploy.sh and the sudoers line)" >&2; exit 1; }

# The deploy user: no password, no shell use beyond deploy.sh.
id forestbotdeploy >/dev/null 2>&1 || useradd --system --create-home --shell /bin/sh forestbotdeploy

mkdir -p /opt/forest-bot/app/releases /var/lib/forest-bot/embed /var/lib/forest-bot/code
install -m 755 -o root -g root "$HERE/deploy.sh" /opt/forest-bot/deploy.sh
install -m 644 -o root -g root "$HERE/compose.yaml" /opt/forest-bot/compose.yaml
chown -R forestbotdeploy:forestbotdeploy /opt/forest-bot/app

# The embedding model (BAAI bge-small-en-v1.5, MIT, ~130 MB): local search,
# nothing sent anywhere to search.
EMBED=/var/lib/forest-bot/embed
HF=https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main
[ -s "$EMBED/model.onnx" ] || curl -fL --retry 3 -o "$EMBED/model.onnx" "$HF/onnx/model.onnx"
[ -s "$EMBED/vocab.txt" ] || curl -fL --retry 3 -o "$EMBED/vocab.txt" "$HF/vocab.txt"

# The container runs as the image's `app` user (uid 1654): the data is its.
chown -R 1654:1654 /var/lib/forest-bot
chmod 700 /var/lib/forest-bot

if [ ! -f /opt/forest-bot/.env ]; then
  umask 077
  cat > /opt/forest-bot/.env <<'EOF'
# The knowledge bot's secrets and settings (bot/README.md). Edit, then:
#   cd /opt/forest-bot && sudo docker compose up -d --force-recreate
FOREST_BOT_DISCORD_TOKEN=
GEMINI_API_KEY=
MISTRAL_API_KEY=
FOREST_BOT_MODELS=gemini:gemini-3.8-flash,gemini:gemini-3.5-flash-lite,mistral:mistral-medium-latest
# Channel ids the bot answers in, comma-separated (empty = every channel it can read)
FOREST_BOT_CHANNELS=
# Where research-queue items are posted (a private channel id; empty = stored only)
FOREST_BOT_QUEUE_CHANNEL=
FOREST_BOT_PER_HOUR=15
FOREST_BOT_PER_DAY=60
EOF
  umask 022
fi

echo "forestbotdeploy ALL=(root) NOPASSWD: /usr/bin/docker restart forest-bot" > /etc/sudoers.d/forest-bot
chmod 440 /etc/sudoers.d/forest-bot
visudo -cf /etc/sudoers.d/forest-bot >/dev/null

# The deploy key: made here, public half locked to deploy.sh, private half
# printed once for GitHub and then deleted from this machine.
SSHDIR=/home/forestbotdeploy/.ssh
mkdir -p "$SSHDIR"
NEWKEY=""
if [ ! -f "$SSHDIR/authorized_keys" ]; then
  TMP="$(mktemp -d)"
  ssh-keygen -q -t ed25519 -N "" -C "github-actions forest-bot" -f "$TMP/key"
  echo "command=\"/opt/forest-bot/deploy.sh\",no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding $(cat "$TMP/key.pub")" > "$SSHDIR/authorized_keys"
  NEWKEY="$(cat "$TMP/key")"
  rm -rf "$TMP"
fi
chown -R forestbotdeploy:forestbotdeploy "$SSHDIR"
chmod 700 "$SSHDIR"; chmod 600 "$SSHDIR/authorized_keys"

cd /opt/forest-bot
if [ -e /opt/forest-bot/app/current ]; then "$DOCKER" compose up -d --force-recreate && echo "forest-bot recreated and started."
else "$DOCKER" compose up --no-start; fi

echo
echo "================ for GitHub: repo Settings -> Secrets and variables -> Actions ================"
echo "(DEPLOY_HOST and DEPLOY_HOST_KEY are the site's - already set)"
if [ -n "$NEWKEY" ]; then
  echo "BOT_DEPLOY_KEY   = everything below, BEGIN and END lines included (shown once, not kept here):"
  echo "$NEWKEY"
else
  echo "BOT_DEPLOY_KEY   = unchanged (made on an earlier run; delete $SSHDIR/authorized_keys and re-run for a new one)"
fi
echo "==========================================================================================="
echo "Next: fill in /opt/forest-bot/.env (sudo nano /opt/forest-bot/.env) and copy the game's code (bot/deploy/README.md)."
