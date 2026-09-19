import json
import re
import time
import urllib.request
import os

KEY = os.environ.get("EMBY_API_KEY", "")
EMBY = "http://localhost:8096/emby"
BODY = json.dumps({
    "MetadataRefreshMode": "FullRefresh",
    "ImageRefreshMode": "None",
    "ReplaceAllMetadata": True,
    "ReplaceAllImages": False,
}).encode()


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "pass2"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def cjk_ratio(text):
    if not text:
        return 0.0
    return len(re.findall(r"[\u4e00-\u9fff]", text)) / len(text)


items = get(f"{EMBY}/Items?IncludeItemTypes=MusicArtist&Recursive=true&Limit=200&Fields=Overview&api_key={KEY}")["Items"]
todo = [i for i in items if cjk_ratio((i.get("Overview") or "").strip()) <= 0.3]
print(f"待重试 {len(todo)} / {len(items)}", flush=True)

for index, item in enumerate(todo):
    try:
        req = urllib.request.Request(
            f"{EMBY}/Items/{item['Id']}/Refresh?api_key={KEY}",
            data=BODY, headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(req, timeout=60) as resp:
            pass
    except Exception as exc:  # noqa: BLE001
        print("  ERR", item.get("Name"), exc, flush=True)
    if (index + 1) % 5 == 0:
        print(f"  queued {index + 1}/{len(todo)}", flush=True)
        time.sleep(6)

print("done", flush=True)
