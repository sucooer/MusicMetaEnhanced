import json
import re
import time
import urllib.request
import os

KEY = os.environ.get("EMBY_API_KEY", "")
URL = (
    "http://localhost:8096/emby/Items?IncludeItemTypes=MusicArtist&Recursive=true&Limit=200"
    f"&Fields=Overview&api_key={KEY}"
)

BASE = "http://localhost:8096/emby/Items?IncludeItemTypes=MusicArtist&Recursive=true&Limit=200&Fields=Overview&api_key="


def fetch():
    req = urllib.request.Request(URL, headers={"User-Agent": "survey"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def is_chinese(text):
    if not text:
        return False
    cjk = len(re.findall(r"[\u4e00-\u9fff]", text))
    return cjk / max(len(text), 1) > 0.3


items = None
for _ in range(40):
    items = fetch().get("Items", [])
    pending = [i for i in items if not (i.get("Overview") or "").strip()]
    print(f"waiting... {len(items) - len(pending)}/{len(items)} have a biography", flush=True)
    if not pending:
        break
    time.sleep(15)

zh = en = 0
rows = []
for i in items:
    ov = (i.get("Overview") or "").strip()
    if not ov:
        continue
    if is_chinese(ov):
        zh += 1
        rows.append((i.get("Name"), "中文", len(ov), ov[:46].replace("\n", " ")))
    else:
        en += 1
        rows.append((i.get("Name"), "非中文", len(ov), ov[:46].replace("\n", " ")))

print()
print(f"{'艺人':<22} {'语言':<8} {'长度':<6} 简介开头")
print("-" * 100)
for name, lang, length, head in sorted(rows):
    print(f"{name:<22} {lang:<8} {length:<6} {head}")

empty = [i.get("Name") for i in items if not (i.get("Overview") or "").strip()]
print("-" * 100)
print(f"共 {len(items)} 位艺人：中文简介 {zh}，非中文 {en}，仍为空 {len(empty)}")
if empty:
    print("无简介：", ", ".join(empty[:20]))
