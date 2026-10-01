#!/usr/bin/env bash
#
# Assert that a built plugin DLL is really loaded and accepted by a real Emby
# server.
#
# Why a real server: Emby discovers plugins only while it starts up. It scans
# <programdata>/plugins/*.dll (top level only, skipping names that begin with
# "."), loads each one, and registers the ones that expose an IPlugin type. A DLL
# can be perfectly valid and still never show up in Dashboard -> Plugins - which
# is exactly the failure people hit after copying a file in without restarting.
#
# Why the .deb and not a Docker image: the official arm64 server package is
# published on GitHub releases, right next to the reference assemblies this repo
# already downloads, so it adds no new dependency and no registry rate limit. It
# also pins the server version exactly, which matters when the whole point is
# "does this DLL load on *that* server". Extracted with dpkg-deb it runs from a
# temporary directory without touching the host.
#
# What is checked, and how (both are hard gates):
#
#   1. the startup log says the assembly was loaded.
#      Emby.Server.Implementations.ApplicationHost loads every assembly found in
#      the plugins directory and logs one line per assembly:
#        Logger.Info("Loading {0} from {1}", assembly.FullName, path)    // loaded
#        Logger.ErrorException("Error loading assembly {0}", ex, path)   // failed
#      So "Loading <name>, Version=..., from <programdata>/plugins/<file>.dll"
#      proves the bytes on disk were read and loaded *on this architecture*, and
#      the absence of "Error loading assembly" proves nothing was skipped.
#
#   2. the server itself lists the plugin.
#      GET /Plugins returns the list the dashboard shows - the answer to "did
#      Emby accept it". The log cannot answer that: the app-info banner that ends
#      with the plugin list is written when a log file is opened, and a freshly
#      started server opens that file *before* it scans the plugins directory, so
#      on a new programdata the banner always lists nothing. (In a long-running
#      server the populated list appears in the rotated file written at local
#      midnight.) That is why this script asks the running server instead of
#      grepping the log for the display name.
#      GET /Plugins needs an admin token, so the script walks the first-run
#      wizard (POST /Startup/Configuration, /Startup/User, /Startup/Complete) and
#      then authenticates (POST /Users/AuthenticateByName). A browser does the
#      same thing on a brand new server, and those calls are accepted without a
#      token from the local machine while the wizard is incomplete.
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
#   EMBY_TIMEOUT_S    budget for the server to answer on HTTP (default: 420)
#   EMBY_BASE_URL     where the server answers (default: http://127.0.0.1:8096)
#   EMBY_PROBE_S      how long a launch must survive to count as up (default: 12)
#   EMBY_LOAD_BUDGET_S  budget for the assembly to show up in the log (default: 120)
#   DOTNET            dotnet command (default: dotnet)
#
# Exit codes: 0 = loaded and listed, 1 = the server did not accept the plugin,
#             2 = the check itself could not run.

set -euo pipefail

dll=${1:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
emby_version=${2:?usage: verify-plugin-load.sh <plugin.dll> <emby-version> [timeout-seconds]}
timeout_s=${3:-${EMBY_TIMEOUT_S:-420}}

plugin_name=${EMBY_PLUGIN_NAME:-Music Meta Enhanced}
arch=${EMBY_ARCH:-arm64}
dotnet_cmd=${DOTNET:-dotnet}
base_url=${EMBY_BASE_URL:-http://127.0.0.1:8096}
admin_user=${EMBY_VERIFY_USER:-emby-verify}
admin_pass=${EMBY_VERIFY_PASS:-EmbyVerify-2026}
# How long a launch must survive before it counts as "the server is up". A
# rejected launch (missing runtime, wrong native library) dies in well under a
# second, so this only has to outlast immediate failure, not full startup.
probe_s=${EMBY_PROBE_S:-12}
# How long the plugin assembly may take to show up in the log once the server is
# answering: discovery runs before the HTTP host is up, so this is short.
load_budget_s=${EMBY_LOAD_BUDGET_S:-120}

[ -f "$dll" ] || { echo "::error::plugin DLL not found: $dll"; exit 2; }
dll=$(cd "$(dirname "$dll")" && pwd)/$(basename "$dll")
dll_base=$(basename "$dll" .dll)
dll_name=$(basename "$dll")

work=$(mktemp -d)
programdata="$work/programdata"
plugins_dir="$programdata/plugins"
server_out="$work/server.out"
log_file="$programdata/logs/embyserver.txt"
body_file="$work/body"
server_pid=""

cleanup() {
  if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
  fi
  # Completing the wizard can make the server restart itself, so make sure no
  # process from this temporary programdata survives the script.
  pkill -f "$programdata" 2>/dev/null || true
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
    | grep -o '"name": *"[^"]*"' | grep -i -e "$arch" -e deb | sed -n '1,20p' || true
  exit 2
fi

# ---------------------------------------------------------------- extract -----
# dpkg-deb -x unpacks the payload only: no maintainer scripts, no root needed.
dpkg-deb -x "$deb" "$work/root" >/dev/null
system_dir="$work/root/opt/emby-server/system"
lib_dir="$work/root/opt/emby-server/lib"
[ -f "$system_dir/EmbyServer.dll" ] || {
  echo "::error::no EmbyServer.dll in $system_dir"
  find "$work/root" -maxdepth 4 -name 'Emby*.dll' | sed -n '1,20p' || true
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
  tr -d ' \n' <"$system_dir/EmbyServer.runtimeconfig.json" | sed -n '1p' | cut -c1-400
  echo
fi
if [ -d "$lib_dir" ]; then
  echo "  lib dir:      $(ls -1 "$lib_dir" | wc -l) files"
  ls -1 "$lib_dir" | sed -n '1,15p' | sed 's/^/    /'
fi

# ---------------------------------------------------------------- install -----
# The DLL has to be in place *before* the server starts: Emby has no plugin
# hot-reload and no rescan, so a copied-in file only takes effect on restart.
mkdir -p "$plugins_dir"
cp "$dll" "$plugins_dir/"
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
  sed -n '1,12p' "$server_out" | sed 's/^/    /'
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

# ---------------------------------------------------------------- http up -----
# The HTTP host comes up before the server has finished starting, but it is the
# only liveness signal that survives the restart the wizard can trigger, so poll
# it until it answers, then wait for the log.
echo "----- waiting for $base_url -----"
http_code=""
http_body=""

http() { # http <method> <path> [json-or-empty] [extra curl args...]
  local method=$1 path=$2 data=${3:-}
  shift 3
  local args=(-sS -X "$method" -o "$body_file" -w '%{http_code}' --max-time 60)
  if [ -n "$data" ]; then
    args+=(-H 'Content-Type: application/json' --data "$data")
  fi
  if [ "$#" -gt 0 ]; then
    args+=("$@")
  fi
  if http_code=$(curl "${args[@]}" "$base_url$path" 2>"$work/curl.err"); then
    :
  else
    http_code=000
  fi
  http_body=$(cat "$body_file" 2>/dev/null || true)
}

ready=0
deadline=$((SECONDS + timeout_s))
while [ "$SECONDS" -lt "$deadline" ]; do
  http GET /System/Info/Public ""
  if [ "$http_code" = "200" ]; then
    ready=1
    echo "  answered: $http_body"
    break
  fi
  if ! kill -0 "$server_pid" 2>/dev/null; then
    echo "::error::the Emby server exited before it answered on $base_url"
    echo "----- server output -----"
    sed -n '1,60p' "$server_out" 2>/dev/null || true
    exit 2
  fi
  sleep 5
done

if [ "$ready" != 1 ]; then
  echo "::error::the Emby server never answered on $base_url within ${timeout_s}s"
  echo "----- log tail -----"
  tail -n 40 "$log_file" 2>/dev/null || true
  exit 2
fi

# ---------------------------------------------------------------- gate 1 ------
# The startup log: Emby read the file and loaded the assembly on this CPU.
echo "----- gate 1: the assembly appears in the startup log -----"
load_line=""
load_deadline=$((SECONDS + load_budget_s))
while [ "$SECONDS" -lt "$load_deadline" ]; do
  if [ -s "$log_file" ] && grep -aqF "Loading $dll_base," "$log_file"; then
    load_line=$(grep -am1 -F "Loading $dll_base," "$log_file")
    break
  fi
  sleep 5
done

if [ -z "$load_line" ]; then
  echo "::error::Emby never logged that it loaded $dll_name - the assembly was not picked up"
  echo "----- lines mentioning this plugin -----"
  grep -aF "$dll_base" "$log_file" 2>/dev/null | sed -n '1,20p' || echo "(none)"
  echo "----- what the server sees -----"
  ls -l "$plugins_dir" 2>&1 || true
  echo "----- log tail -----"
  tail -n 60 "$log_file" 2>/dev/null || true
  exit 1
fi

case "$load_line" in
  *"$plugins_dir/$dll_name"*)
    echo "  $load_line"
    ;;
  *)
    echo "::error::Emby loaded this assembly from somewhere else: $load_line"
    exit 1
    ;;
esac

if loader_errors=$(grep -aE 'Error loading assembly|Error loading types from assembly|Could not load file or assembly|LoaderException' "$log_file"); then
  echo "::error::the server reported plugin load errors"
  printf '%s\n' "$loader_errors"
  exit 1
fi
echo "  no plugin load errors in the log"

# ---------------------------------------------------------------- gate 2 ------
# The server's own plugin list. First run the setup wizard so the admin token
# needed by GET /Plugins can be obtained; a brand new programdata has no users.
echo "----- gate 2: the server's own plugin list -----"

step() { # step <label> <method> <path> [json-or-empty] [extra curl args...]
  local label=$1
  shift
  http "$@"
  local short=${http_body//$'\n'/ }
  printf '  %-34s HTTP %-4s %s\n' "$label" "$http_code" "${short:0:200}"
  case "$http_code" in
    200 | 204) return 0 ;;
    *) return 1 ;;
  esac
}

# The authorization header is client identification, not a token: send it on the
# wizard calls too, exactly like the web client does.
auth_header="Emby UserId=\"\", Client=\"plugin-load-verify\", Device=\"GitHub Actions\", DeviceId=\"plugin-load-verify\", Version=\"1.0.0\""

wizard_ok=1
step "POST /Startup/Configuration" POST /Startup/Configuration \
  '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' \
  -H "X-Emby-Authorization: $auth_header" \
  || wizard_ok=0
step "POST /Startup/User" POST /Startup/User \
  "{\"Name\":\"$admin_user\",\"Password\":\"$admin_pass\"}" \
  -H "X-Emby-Authorization: $auth_header" \
  || wizard_ok=0
step "POST /Startup/Complete" POST /Startup/Complete "" \
  -H "X-Emby-Authorization: $auth_header" \
  || wizard_ok=0
token=""
attempt=1
while [ "$attempt" -le 5 ] && [ -z "$token" ]; do
  http POST /Users/AuthenticateByName \
    "{\"Username\":\"$admin_user\",\"Pw\":\"$admin_pass\"}" \
    -H "X-Emby-Authorization: $auth_header"
  printf '  %-34s HTTP %-4s\n' "POST /Users/AuthenticateByName" "$http_code"
  if [ "$http_code" = "200" ]; then
    token=$(printf '%s' "$http_body" | sed -n 's/.*"AccessToken":"\([^"]*\)".*/\1/p')
  fi
  [ -n "$token" ] && break
  attempt=$((attempt + 1))
  sleep 5
done

listed=0
plugins_json=""
if [ -n "$token" ]; then
  attempt=1
  while [ "$attempt" -le 10 ]; do
    http GET /Plugins "" -H "X-Emby-Token: $token"
    printf '  %-34s HTTP %-4s\n' "GET /Plugins" "$http_code"
    if [ "$http_code" = "200" ]; then
      plugins_json=$http_body
      case "$plugins_json" in
        *"\"Name\":\"$plugin_name\""*) listed=1 ;;
      esac
      break
    fi
    attempt=$((attempt + 1))
    sleep 5
  done
fi

echo "----- plugin list reported by the server -----"
if [ -n "$plugins_json" ]; then
  printf '%s' "$plugins_json" | sed 's/},{/}\n{/g' | sed -n '1,60p'
else
  echo "(could not read the plugin list)"
fi

if [ "$listed" = 1 ]; then
  echo "PASS: Emby $emby_version ($(uname -m)) loaded $dll_name and lists \"$plugin_name\"."
  exit 0
fi

# Say why it could not be checked, rather than claiming the plugin is broken.
if [ -z "$token" ]; then
  if [ "$wizard_ok" != 1 ]; then
    echo "::error::the first-run wizard calls failed (see the HTTP statuses above)"
  else
    echo "::error::could not authenticate against the freshly started server (wizard status above)"
  fi
  exit 2
fi

echo "::error::Emby $emby_version ($(uname -m)) does not list \"$plugin_name\" - the plugin was not accepted"
echo "----- server -----"
grep -am1 'Emby Server Version' "$log_file" 2>/dev/null || echo "(no startup banner)"
echo "----- lines mentioning this plugin -----"
grep -aF "$dll_base" "$log_file" 2>/dev/null | sed -n '1,20p' || echo "(none)"
echo "----- loader errors -----"
grep -aE 'Error loading assembly|Error loading types from assembly|LoaderException|Error in IsExportType|Error loading plugin|Not loading ' "$log_file" 2>/dev/null | sed -n '1,40p' || true
echo "----- app-info plugin list (expected to be empty on a fresh programdata) -----"
awk '
  /Plugins:[[:space:]]*$/ { inlist = 1; print; next }
  inlist && /^[0-9][0-9][0-9][0-9]-/ { exit }
  inlist { print }
' "$log_file" 2>/dev/null | sed -n '1,60p' || true
echo "----- what the server sees -----"
ls -l "$plugins_dir" 2>&1 || true
echo "----- server output -----"
tail -n 40 "$server_out" 2>/dev/null || true
echo "----- log tail -----"
tail -n 60 "$log_file" 2>/dev/null || true
exit 1
