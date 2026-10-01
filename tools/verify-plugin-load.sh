#!/usr/bin/env bash
#
# Assert that a built plugin DLL is really loaded by a real Emby server.
#
# Why a real server: Emby discovers plugins only while it starts up. It scans
# <programdata>/plugins/*.dll (top level only, skipping names that begin with
# "."), loads each one, and prints the survivors in its startup log. A DLL can be
# perfectly valid and still never appear in Dashboard -> Plugins - which is
# exactly the failure people hit after copying a file in without restarting.
#
# Why the .deb and not a Docker image: the official arm64 server package is
# published on GitHub releases, right next to the reference assemblies this repo
# already downloads, so it adds no new dependency and no registry rate limit. It
# also pins the server version exactly, which matters when the whole point is
# "does this DLL load on *that* server". Extracted with dpkg-deb it runs from a
# temporary directory without touching the host.
#
# Requires: Linux on the target architecture (aarch64 for arm64), curl, dpkg-deb,
#           and a .NET 8 runtime reachable through `dotnet`.
#
# Usage:
#   tools/verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]
#
# Environment:
#   EMBY_PLUGIN_NAME  name as Emby lists it (default: Music Meta Enhanced)
#   EMBY_ARCH         server package architecture (default: arm64)
#   EMBY_TIMEOUT_S    startup budget in seconds (default: 420)
#   DOTNET            dotnet command (default: dotnet)
#
# Exit codes: 0 = the server listed the plugin, 1 = it did not, 2 = could not run.

set -euo pipefail

dll=${1:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
emby_version=${2:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
timeout_s=${3:-${EMBY_TIMEOUT_S:-420}}

plugin_name=${EMBY_PLUGIN_NAME:-Music Meta Enhanced}
arch=${EMBY_ARCH:-arm64}
dotnet_cmd=${DOTNET:-dotnet}
# How long a launch must survive before it counts as "the server is up". A
# rejected launch (missing runtime, wrong native library) dies in well under a
# second, so this only has to outlast immediate failure, not full startup.
probe_s=${EMBY_PROBE_S:-12}

[ -f "$dll" ] || { echo "::error::plugin DLL not found: $dll"; exit 2; }
dll=$(cd "$(dirname "$dll")" && pwd)/$(basename "$dll")
dll_base=$(basename "$dll" .dll)

work=$(mktemp -d)
programdata="$work/programdata"
server_out="$work/server.out"
log_file="$programdata/logs/embyserver.txt"
server_pid=""

cleanup() {
  if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT

echo "Verifying that Emby $emby_version loads $dll"
echo "  host arch: $(uname -m)"
echo "  dotnet:    $("$dotnet_cmd" --version 2>/dev/null || echo '(not found)')"
"$dotnet_cmd" --list-runtimes 2>/dev/null | grep -i 'Microsoft.NETCore.App 8' | sed 's/^/  runtime:   /' \
  || echo "  runtime:   no Microsoft.NETCore.App 8.x found"

# ---------------------------------------------------------------- download ----
asset="emby-server-deb_${emby_version}_${arch}.deb"
url="https://github.com/MediaBrowser/Emby.Releases/releases/download/${emby_version}/${asset}"
deb="$work/$asset"

echo "Downloading $url"
if ! curl -fsSL --retry 3 --retry-delay 5 -o "$deb" "$url"; then
  echo "::error::could not download the Emby $emby_version $arch server package ($asset)"
  # Say what the release actually offers: a renamed asset is the usual reason.
  curl -fsSL --max-time 30 \
    "https://api.github.com/repos/MediaBrowser/Emby.Releases/releases/tags/${emby_version}" \
    | grep -o '"name": *"[^"]*"' | grep -i -e "$arch" -e deb | head -20 || true
  exit 2
fi

# ---------------------------------------------------------------- extract -----
# dpkg-deb -x unpacks the payload only: no maintainer scripts, no root needed.
dpkg-deb -x "$deb" "$work/root" >/dev/null
system_dir="$work/root/opt/emby-server/system"
lib_dir="$work/root/opt/emby-server/lib"
[ -f "$system_dir/EmbyServer.dll" ] || {
  echo "::error::no EmbyServer.dll in $system_dir"
  find "$work/root" -maxdepth 4 -name 'Emby*.dll' | head -20 || true
  exit 2
}

# The server package can be laid out in more than one way across versions (a
# self-contained apphost, or a framework-dependent DLL plus native libraries),
# and the wrong assumption fails in ways that look nothing like the cause. So
# record what is actually there and let the launch attempts below pick.
echo "----- package layout -----"
echo "  system dir:   $system_dir"
[ -x "$system_dir/EmbyServer" ] && echo "  apphost:      yes" || echo "  apphost:      no"
if [ -f "$system_dir/EmbyServer.runtimeconfig.json" ]; then
  tr -d ' \n' <"$system_dir/EmbyServer.runtimeconfig.json" | head -c 400
  echo
fi
[ -d "$lib_dir" ] && { echo "  lib dir:      $(ls -1 "$lib_dir" | wc -l) files"; ls -1 "$lib_dir" | sed -n '1,15p' | sed 's/^/    /'; }

# ---------------------------------------------------------------- install -----
# The DLL has to be in place *before* the server starts: Emby has no plugin
# hot-reload and no rescan, so a copied-in file only takes effect on restart.
mkdir -p "$programdata/plugins"
cp "$dll" "$programdata/plugins/"
chmod -R 0777 "$programdata"

# ---------------------------------------------------------------- launch ------
# Try the ways the package can legitimately be started, in order of how little
# they assume, and keep the first one that stays up. DOTNET_ROLL_FORWARD is
# pinned to 8.0 so the server never lands on a newer major runtime by accident;
# LD_LIBRARY_PATH is only ever set on the last attempt, because pointing it at
# the package's native libraries can break the .NET host itself.
launch() {
  label=$1
  shift
  echo "Launching: $label"
  : >"$server_out"
  (
    cd "$system_dir"
    "$@" -programdata "$programdata" >"$server_out" 2>&1 &
    echo $! >"$work/server.pid"
  )
  server_pid=$(cat "$work/server.pid")
  sleep "$probe_s"
  if kill -0 "$server_pid" 2>/dev/null; then
    echo "  running (pid $server_pid)"
    return 0
  fi
  echo "  exited within ${probe_s}s:"
  head -12 "$server_out" | sed 's/^/    /'
  return 1
}

if [ -x "$system_dir/EmbyServer" ] && launch "packaged apphost" "$system_dir/EmbyServer"; then
  launched=1
elif launch "dotnet (framework-dependent)" env DOTNET_ROLL_FORWARD=LatestPatch \
  "$dotnet_cmd" "$system_dir/EmbyServer.dll"; then
  launched=1
elif [ -d "$lib_dir" ] && launch "dotnet + package native libraries" \
  env DOTNET_ROLL_FORWARD=LatestPatch LD_LIBRARY_PATH="$lib_dir" \
  "$dotnet_cmd" "$system_dir/EmbyServer.dll"; then
  launched=1
else
  launched=0
fi

if [ "$launched" != 1 ]; then
  echo "::error::could not start the Emby $emby_version server from the package"
  exit 2
fi

# ---------------------------------------------------------------- wait --------
listed=0
deadline=$((SECONDS + timeout_s))
while [ "$SECONDS" -lt "$deadline" ]; do
  if ! kill -0 "$server_pid" 2>/dev/null; then
    echo "::error::the Emby server exited before it finished starting"
    echo "----- server output -----"
    tail -n 60 "$server_out" 2>/dev/null || true
    exit 2
  fi

  if [ -s "$log_file" ]; then
    if grep -qiF "$plugin_name" "$log_file"; then
      listed=1
      break
    fi
    # Emby writes the whole plugin list in one multiline entry, right after
    # discovery. Seeing that heading means discovery is over, so give the log a
    # moment to flush and judge then instead of burning the whole budget.
    if grep -qF 'Plugins:' "$log_file"; then
      sleep 10
      grep -qiF "$plugin_name" "$log_file" && listed=1
      break
    fi
  fi
  sleep 5
done

# ---------------------------------------------------------------- report ------
echo "----- server -----"
grep -am1 'Emby Server Version' "$log_file" 2>/dev/null || echo "(no startup banner yet)"
echo "----- plugin list -----"
# The list is a multiline entry: a "Plugins:" heading followed by one indented
# "name version" line per plugin, ending at the next timestamped log line.
awk '
  /Plugins:[[:space:]]*$/ { inlist = 1; print; next }
  inlist && /^[0-9][0-9][0-9][0-9]-/ { exit }
  inlist { print }
' "$log_file" 2>/dev/null | head -60 || true
echo "----- lines mentioning this plugin -----"
grep -aF "$dll_base" "$log_file" 2>/dev/null | head -20 || echo "(none)"
echo "----- loader errors -----"
grep -aE 'Error loading assembly|Error loading types from assembly|LoaderException|Error in IsExportType|Error loading plugin|Not loading ' "$log_file" 2>/dev/null | head -40 || true

if [ "$listed" = 1 ]; then
  echo "PASS: Emby $emby_version ($(uname -m)) lists \"$plugin_name\"."
  exit 0
fi

echo "::error::Emby $emby_version did not list \"$plugin_name\" - the plugin was not loaded."
echo "----- what the server sees -----"
ls -l "$programdata/plugins" 2>&1 || true
echo "----- server output -----"
tail -n 40 "$server_out" 2>/dev/null || true
echo "----- log tail -----"
tail -n 60 "$log_file" 2>/dev/null || true
exit 1
