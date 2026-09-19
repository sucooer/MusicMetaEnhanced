"""Probe Apple Music search result pages: dump sections and artist items.

Usage: python am_search_probe.py <term> [storefront ...]
Prints the final URL (to reveal geo redirects) and every artist item found.
"""

import json
import re
import sys
import urllib.parse
import urllib.request

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)


def fetch(url):
    # Bypass the system proxy: it cannot reach music.apple.com from here.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with opener.open(req, timeout=45) as resp:
        return resp.geturl(), resp.read().decode("utf-8", errors="replace")


def parse_sections(html):
    m = re.search(
        r'<script[^>]*id="serialized-server-data"[^>]*>(.*?)</script>', html, re.S
    )
    if not m:
        return None
    try:
        return json.loads(m.group(1))["data"][0]["data"].get("sections") or []
    except Exception as exc:  # noqa: BLE001
        print("   解析失败:", exc)
        return None


def describe(item):
    cd = item.get("contentDescriptor") or {}
    identifiers = cd.get("identifiers") or {}
    return (
        f"{item.get('title')!r} kind={cd.get('kind')!r} "
        f"url={cd.get('url')!r} id={identifiers.get('storeAdamID')!r}"
    )


term = sys.argv[1]
storefronts = sys.argv[2:] or ["cn"]

for sf in storefronts:
    url = f"https://music.apple.com/{sf}/search?term={urllib.parse.quote(term)}"
    print(f"===== {sf}: {url}")
    try:
        final_url, html = fetch(url)
    except Exception as exc:  # noqa: BLE001
        print("   请求失败:", exc)
        continue
    print(f"   实际落地: {final_url}")
    secs = parse_sections(html)
    if secs is None:
        continue
    for i, sec in enumerate(secs):
        sid = sec.get("id")
        items = sec.get("items") or []
        if not items:
            continue
        print(f"   [{i}] id={sid!r} itemKind={sec.get('itemKind')!r} items={len(items)}")
        for it in items[:10]:
            print("        -", describe(it))
