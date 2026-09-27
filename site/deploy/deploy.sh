#!/bin/sh
# The ONLY thing the GitHub deploy key can run (a forced command in
# ~forestdeploy/.ssh/authorized_keys). Reads a release tarball on stdin,
# unpacks it beside the others, points `current` at it, restarts the
# container and keeps the last three releases.
set -eu
APP=/opt/forest-site/app
STAMP=$(date -u +%Y%m%d-%H%M%S)
NEW="$APP/releases/$STAMP"

mkdir -p "$NEW"
if ! head -c 300000000 | tar xzf - -C "$NEW"; then rm -rf "$NEW"; echo "bad tarball" >&2; exit 1; fi
if [ ! -f "$NEW/ForestSite.dll" ]; then rm -rf "$NEW"; echo "no ForestSite.dll in the tarball" >&2; exit 1; fi

ln -sfn "releases/$STAMP" "$APP/current.new"
mv -Tf "$APP/current.new" "$APP/current"
sudo -n /usr/bin/docker restart forest-site >/dev/null

ls -1d "$APP"/releases/* | sort | head -n -3 | xargs -r rm -rf
echo "deployed $STAMP"
