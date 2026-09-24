#!/usr/bin/env bash
# The other end of the live test bridge (src/Modules/BridgeModule.cs).
#
#   scripts/bridge.sh 'status' 'find _Dummy 100'
#   scripts/bridge.sh -f commands.txt
#   scripts/bridge.sh -t 300 'restore my-state load' 'find mutant 60'
#
# Writes the commands to bridge/in.txt in one rename, then waits until
# the game has run them all and prints the replies. Needs the game
# running with Settings -> "Test bridge" ticked (or TestBridge = true
# under [Diagnostics] in the plugin's .cfg).
#
# The bridge folder: $FOREST_BRIDGE, else <FOREST_ROOT>/BepInEx/config/
# ForestOverlay/bridge (FOREST_ROOT is User-scope on the author's machine
# and not inherited by tool shells, so it is read from the registry).
set -euo pipefail

timeout=60
file=""
while getopts "t:f:" opt; do
  case $opt in
    t) timeout=$OPTARG ;;
    f) file=$OPTARG ;;
    *) echo "usage: $0 [-t seconds] [-f file] [command ...]" >&2; exit 2 ;;
  esac
done
shift $((OPTIND - 1))

dir=${FOREST_BRIDGE:-}
if [ -z "$dir" ]; then
  root=${FOREST_ROOT:-}
  if [ -z "$root" ] && command -v powershell.exe >/dev/null; then
    root=$(powershell.exe -NoProfile -Command "[Environment]::GetEnvironmentVariable('FOREST_ROOT','User')" | tr -d '\r')
  fi
  root=${root:-'G:\SteamLibrary\steamapps\common\The Forest'}
  dir="$(cygpath -u "$root" 2>/dev/null || echo "$root")/BepInEx/config/ForestOverlay/bridge"
fi
mkdir -p "$dir"
in="$dir/in.txt"
out="$dir/out.txt"

# Still waiting from a previous call: the game is not reading.
for _ in $(seq 1 20); do [ -e "$in" ] || break; sleep 0.25; done
if [ -e "$in" ]; then
  echo "bridge: $in is still there - is the game running with the Test bridge ticked (Settings)?" >&2
  exit 1
fi

marker="__bridge_done_$$_$RANDOM"
start=$( [ -e "$out" ] && stat -c %s "$out" || echo 0 )

{
  if [ -n "$file" ]; then cat "$file"; echo; fi
  for c in "$@"; do printf '%s\n' "$c"; done
  printf 'echo %s\n' "$marker"
} > "$dir/in.tmp"
mv -f "$dir/in.tmp" "$in"

deadline=$(( $(date +%s) + timeout ))
while :; do
  if [ -e "$out" ]; then
    size=$(stat -c %s "$out")
    [ "$size" -lt "$start" ] && start=0   # rotated to out.old.txt
    if tail -c +$((start + 1)) "$out" | grep -q "$marker"; then break; fi
  fi
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "bridge: no end marker after ${timeout}s - replies so far:" >&2
    [ -e "$out" ] && tail -c +$((start + 1)) "$out"
    [ -e "$in" ] && echo "bridge: in.txt was never read (game not running, bridge off, or still loading)" >&2
    exit 1
  fi
  sleep 0.3
done

tail -c +$((start + 1)) "$out" | grep -v "$marker"
