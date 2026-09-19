import json
import re
import sys

path = sys.argv[1]
dot_path = sys.argv[2] if len(sys.argv) > 2 else ""
max_depth = int(sys.argv[3]) if len(sys.argv) > 3 else 8

html = open(path, encoding="utf-8", errors="replace").read()
m = re.search(r'<script[^>]*id="serialized-server-data"[^>]*>(.*?)</script>', html, re.S)
if not m:
    print("no serialized-server-data found")
    sys.exit(1)

data = json.loads(m.group(1))

node = data
for part in [p for p in dot_path.split(".") if p]:
    if part.isdigit() and isinstance(node, list):
        node = node[int(part)]
    else:
        node = node[part]


def walk(n, depth=0):
    if depth > max_depth:
        return
    if isinstance(n, dict):
        for k, v in n.items():
            if isinstance(v, (dict, list)):
                print(f"{' ' * depth}{k}: ({type(v).__name__}, len={len(v)})")
                walk(v, depth + 1)
            else:
                s = str(v)
                if len(s) > 120:
                    s = s[:120] + "..."
                print(f"{' ' * depth}{k}: {s}")
    elif isinstance(n, list):
        for i, v in enumerate(n):
            print(f"{' ' * depth}[{i}]")
            walk(v, depth + 1)


walk(node)
