import json
import re
import sys
import time
import urllib.parse
import urllib.request

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
SF = sys.argv[1] if len(sys.argv) > 1 else "cn"
NAMES = sys.argv[2:]


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Language": "zh-CN,zh;q=0.9"})
    with urllib.request.urlopen(req, timeout=45) as resp:
        return resp.read().decode("utf-8", errors="replace")


def page_data(html):
    m = re.search(r'<script[^>]*id="serialized-server-data"[^>]*>(.*?)</script>', html, re.S)
    return json.loads(m.group(1)) if m else None


def sections(data):
    for entry in data.get("data", []):
        inner = entry.get("data") or {}
        if "sections" in inner:
            return inner["sections"]
    return []


print(f"storefront={SF}")
print(f"{'库里的艺人名':<22} {'苹果返回的名字':<26} {'id':<12} bio")
print("-" * 78)

for name in NAMES:
    try:
        shtml = get(f"https://music.apple.com/{SF}/search?term={urllib.parse.quote(name)}")
        sdata = page_data(shtml)
        if not sdata:
            print(f"{name:<22} (搜索页无数据)")
            continue

        artist = None
        for sec in sections(sdata):
            for item in sec.get("items", []) or []:
                cd = item.get("contentDescriptor") or {}
                if cd.get("kind") == "artist":
                    artist = item
                    break
            if artist:
                break

        if not artist:
            print(f"{name:<22} (无艺人结果)")
            continue

        aid = str((artist.get("contentDescriptor") or {}).get("identifiers", {}).get("storeAdamID", ""))
        aname = artist.get("title") or ""

        bio_len = 0
        if aid:
            ahtml = get((artist.get("contentDescriptor") or {}).get("url") or f"https://music.apple.com/{SF}/artist/x/{aid}")
            adata = page_data(ahtml)
            if adata:
                for sec in sections(adata):
                    if sec.get("itemKind") == "artistDetailHeader":
                        for item in sec.get("items", []) or []:
                            bio = item.get("bio")
                            bio_len = len(bio) if bio else 0
                        break

        print(f"{name:<22} {aname:<26} {aid:<12} {'有 ' + str(bio_len) + ' 字' if bio_len else '无'}")
    except Exception as exc:  # noqa: BLE001
        print(f"{name:<22} ERROR {exc}")
    time.sleep(0.6)
