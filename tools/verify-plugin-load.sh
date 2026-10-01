#!/usr/bin/env bash
#
# Assert that a built plugin DLL is really loaded by a real Emby server.
#
# Emby discovers plugins only while it starts up: it scans
# <programdata>/plugins/*.dll (top level only, skipping names that start with
# "."), loads each one, and reports the survivors in its startup log. A DLL can
# be perfectly valid, sit in the right folder, and still never appear in
# Dashboard -> Plugins - which is precisely the failure users hit after copying
# a file in without restarting, or when the server cannot read it.
#
# So this script tests the claim that actually matters: the server loads it.
# It starts a real Emby container with the DLL already in /config/plugins and
# asserts the server lists the plugin.
#
# Usage:
#   tools/verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]
#
# Environment:
#   DOCKER            docker command (default: docker, e.g. "sudo docker")
#   EMBY_IMAGES       image candidates to try in order (default: the official
#                     arm64 image, then the multi-arch name)
#   EMBY_PLATFORM     docker --platform value (default: linux-arm64)
#   EMBY_PLUGIN_NAME  plugin name as Emby lists it (default: Music Meta Enhanced)
#   EMBY_PLUGIN_DIR   container plugin folder (default: /config/plugins)
#   EMBY_LOG_PATH     container log file    (default: /config/logs/embyserver.txt)
#
# Exit codes: 0 = server listed the plugin, 1 = it did not, 2 = no image/container.

set -euo pipefail

dll=${1:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
emby_version=${2:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
timeout_s=${3:-300}

docker_cmd=${DOCKER:-docker}
platform=${EMBY_PLATFORM:-linux-arm64}
plugin_name=${EMBY_PLUGIN_NAME:-Music Meta Enhanced}
plugin_dir=${EMBY_PLUGIN_DIR:-/config/plugins}
log_path=${EMBY_LOG_PATH:-/config/logs/embyserver.txt}
images=${EMBY_IMAGES:-emby/embyserver_arm64v8:${emby_version} emby/embyserver:${emby_version}}

[ -f "$dll" ] || { echo "::error::plugin DLL not found: $dll"; exit 2; }
dll=$(cd "$(dirname "$dll")" && pwd)/$(basename "$dll")
dll_base=$(basename "$dll" .dll)

# Container names must be unique per version so concurrent runs cannot collide.
container="emby-plugin-verify-$(printf '%s' "$emby_version" | tr -c '0-9a-zA-Z' '-')"
work=$(mktemp -d)
log_copy="$work/embyserver.txt"

cleanup() {
  "$docker_cmd" rm -f "$container" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

echo "Verifying that Emby $emby_version loads $dll"
echo "  docker:   $docker_cmd $("$docker_cmd" version --format '{{.Server.Version}}' 2>/dev/null || echo '?')"
echo "  host arch: $(uname -m)"
echo "  platform: $platform"

# ---------------------------------------------------------------- image -------
image=""
for candidate in $images; do
  echo "Pulling $candidate ..."
  for attempt in 1 2 3; do
    if "$docker_cmd" pull --platform "$platform" "$candidate" >/dev/null 2>&1; then
      image=$candidate
      break
    fi
    echo "  attempt $attempt failed"
    sleep $((attempt * 10))
  done
  [ -n "$image" ] && break
done

if [ -z "$image" ]; then
  echo "::error::no Emby $emby_version image available (tried: $images)"
  # Tell the next reader what tags the registry actually has. $images starts
  # with "namespace/repository:tag", so dropping everything from the colon
  # leaves the full "namespace/repository" the API wants.
  repo=${images%%:*}
  curl -fsSL --max-time 30 \
    "https://hub.docker.com/v2/repositories/${repo}/tags?page_size=100&name=${emby_version}" \
    | head -c 2000 || true
  echo
  exit 2
fi
echo "Using image: $image"

# ---------------------------------------------------------------- start -------
# The DLL has to be in place *before* the server starts: Emby has no plugin
# hot-reload and no rescan, so a copied-in file only takes effect on restart.
mkdir -p "$work/config/plugins"
cp "$dll" "$work/config/plugins/"
# The image may run Emby as a non-root user, so make the folder world readable.
chmod -R 0777 "$work/config"

"$docker_cmd" run -d --name "$container" --platform "$platform" \
  -v "$work/config:/config" "$image" >/dev/null

container_arch=$("$docker_cmd" exec "$container" uname -m 2>/dev/null || echo '?')
echo "  container arch: $container_arch"

# ---------------------------------------------------------------- wait --------
listed=0
deadline=$((SECONDS + timeout_s))
while [ "$SECONDS" -lt "$deadline" ]; do
  if ! "$docker_cmd" inspect -f '{{.State.Running}}' "$container" 2>/dev/null | grep -q true; then
    echo "::error::the Emby container exited before it finished starting"
    "$docker_cmd" logs --tail 100 "$container" 2>&1 || true
    exit 2
  fi

  if "$docker_cmd" exec "$container" test -f "$log_path" 2>/dev/null; then
    "$docker_cmd" cp "$container:$log_path" "$log_copy" >/dev/null 2>&1 || true
    if [ -s "$log_copy" ]; then
      if grep -qiF "$plugin_name" "$log_copy"; then
        listed=1
        break
      fi
      # Emby writes the whole plugin list in one multiline entry, right after
      # discovery. Seeing that heading means discovery is over, so give the log
      # a moment to flush and judge then instead of burning the whole timeout.
      if grep -qF 'Plugins:' "$log_copy"; then
        sleep 10
        "$docker_cmd" cp "$container:$log_path" "$log_copy" >/dev/null 2>&1 || true
        grep -qiF "$plugin_name" "$log_copy" && listed=1
        break
      fi
    fi
  fi
  sleep 5
done

# ---------------------------------------------------------------- report ------
echo "----- server -----"
grep -am1 'Emby Server Version' "$log_copy" 2>/dev/null || echo "(no startup banner yet)"
echo "----- plugin list -----"
# The list is a multiline entry: a "Plugins:" heading followed by one indented
# "name version" line per plugin, ending at the next timestamped log line.
awk '
  /Plugins:[[:space:]]*$/ { inlist = 1; print; next }
  inlist && /^[0-9][0-9][0-9][0-9]-/ { exit }
  inlist { print }
' "$log_copy" 2>/dev/null | head -60 || true
echo "----- lines mentioning this plugin -----"
grep -aF "$dll_base" "$log_copy" 2>/dev/null | head -20 || echo "(none)"
echo "----- loader errors -----"
grep -aE 'Error loading assembly|Error loading types from assembly|LoaderException|Error in IsExportType|Error loading plugin|Not loading ' "$log_copy" 2>/dev/null | head -40 || true

if [ "$listed" = 1 ]; then
  echo "PASS: Emby $emby_version ($container_arch) lists \"$plugin_name\"."
  exit 0
fi

echo "::error::Emby $emby_version did not list \"$plugin_name\" - the plugin was not loaded."
echo "----- what the container sees -----"
"$docker_cmd" exec "$container" sh -c \
  "ls -l '$plugin_dir' 2>&1; echo; ls -l \"\$(dirname '$log_path')\" 2>&1 | head -20; echo; id 2>&1" 2>&1 || true
echo "----- surviving log path search -----"
"$docker_cmd" exec "$container" sh -c \
  'find / -maxdepth 5 -name "embyserver*.txt" 2>/dev/null | head -20' 2>&1 || true
echo "----- log tail -----"
tail -n 60 "$log_copy" 2>/dev/null || true
exit 1
