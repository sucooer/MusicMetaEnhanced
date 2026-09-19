import json
import time
import urllib.request

KEY = "(API key removed)"
EMBY = "http://localhost:8096/emby"
BODY = json.dumps({
    "MetadataRefreshMode": "FullRefresh",
    "ImageRefreshMode": "None",
    "ReplaceAllMetadata": True,
    "ReplaceAllImages": False,
}).encode()

ids = [line.strip() for line in open("artist-ids.txt") if line.strip()]
print(f"refreshing {len(ids)} artists", flush=True)

ok = 0
for index, item_id in enumerate(ids):
    url = f"{EMBY}/Items/{item_id}/Refresh?api_key={KEY}"
    req = urllib.request.Request(url, data=BODY, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            ok += resp.status == 204
    except Exception as exc:  # noqa: BLE001
        print(f"  {item_id} ERROR {exc}", flush=True)

    if (index + 1) % 8 == 0:
        print(f"  queued {index + 1}/{len(ids)}", flush=True)
        time.sleep(8)

print(f"done, {ok}/{len(ids)} accepted", flush=True)
