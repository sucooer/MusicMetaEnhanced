import json
import sys
import time
import urllib.parse
import urllib.request

API = "http://127.0.0.1:3055"


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": "EmbyAppleMusic/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8", errors="replace"))


print(f"{'库里艺人名':<20} {'网易云匹配到':<20} {'briefDesc':<10} 简介前 40 字")
print("-" * 92)

hit = 0
for name in sys.argv[1:]:
    try:
        s = get(f"{API}/search?keywords={urllib.parse.quote(name)}&type=100&limit=5")
        artists = ((s.get("result") or {}).get("artists")) or []
        exact = [a for a in artists if a.get("name") == name]
        target = exact[0] if exact else None
        if not target:
            shown = ", ".join(a.get("name", "") for a in artists[:3]) or "(无结果)"
            print(f"{name:<20} {shown:<20} {'-':<10} 名字不匹配")
            continue
        d = get(f"{API}/artist/desc?id={target['id']}")
        brief = (d.get("briefDesc") or "").strip()
        intro = [b.get("tx") for b in (d.get("introduction") or []) if (b.get("tx") or "").strip()]
        text = intro[0] if intro else brief
        if text:
            hit += 1
        print(f"{name:<20} {target.get('name',''):<20} {len(brief):<10} {(text or '(空)')[:40]}".replace("\n", " "))
    except Exception as exc:  # noqa: BLE001
        print(f"{name:<20} ERROR {exc}")
    time.sleep(0.3)

print("-" * 92)
print(f"命中 {hit}/{len(sys.argv) - 1}")
