import json
import sys
import time
import urllib.parse
import urllib.request

KEY = "2139078587215309723505"
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36"


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8", errors="replace"))


print(f"{'库里艺人名':<20} {'TheAudioDB 名称':<22} CN    EN    JP")
print("-" * 66)

for name in sys.argv[1:]:
    try:
        d = get(f"https://www.theaudiodb.com/api/v1/json/{KEY}/search.php?s={urllib.parse.quote(name)}")
        artists = d.get("artists") or []
        if not artists:
            print(f"{name:<20} {'(无结果)':<22}")
            continue
        a = artists[0]
        cn = len(a.get("strBiographyCN") or "")
        en = len(a.get("strBiographyEN") or "")
        jp = len(a.get("strBiographyJP") or "")
        print(f"{name:<20} {(a.get('strArtist') or '')[:20]:<22} {cn:<5} {en:<5} {jp}")
    except Exception as exc:  # noqa: BLE001
        print(f"{name:<20} ERROR {exc}")
    time.sleep(0.4)
