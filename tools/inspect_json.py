import json
import re
import sys

path = sys.argv[1]
html = open(path, encoding="utf-8", errors="replace").read()

m = re.search(
    r'<script[^>]*id="serialized-server-data"[^>]*>(.*?)</script>',
    html,
    re.S,
)
if not m:
    print("no serialized-server-data found")
    sys.exit(1)

raw = m.group(1)
data = json.loads(raw)


def walk(node, path="", depth=0, limit=6):
    if depth > limit:
        return
    if isinstance(node, dict):
        for k, v in node.items():
            p = f"{path}.{k}"
            if isinstance(v, (dict, list)):
                print(f"{' ' * depth}{p}  ({type(v).__name__}, len={len(v)})")
                walk(v, p, depth + 1, limit)
            else:
                s = str(v)
                if len(s) > 90:
                    s = s[:90] + "..."
                print(f"{' ' * depth}{p} = {s}")
    elif isinstance(node, list):
        for i, v in enumerate(node[:3]):
            walk(v, f"{path}[{i}]", depth + 1, limit)


walk(data)
