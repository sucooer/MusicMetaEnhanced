import json
import sys

# Enables (or removes) the "Apple Music" provider in the Emby music library TypeOptions.
# usage: enable_provider.py vf.json out.json add|remove

src, dst, action = sys.argv[1], sys.argv[2], sys.argv[3]

with open(src, encoding="utf-8") as f:
    folders = json.load(f)

targets = {"MusicAlbum", "MusicArtist"}
name = "Apple Music"

for folder in folders:
    opts = folder.get("LibraryOptions") or {}
    for type_option in opts.get("TypeOptions") or []:
        if type_option.get("Type") not in targets:
            continue
        for key in ("MetadataFetchers", "ImageFetchers"):
            fetchers = type_option.get(key) or []
            if action == "add" and name not in fetchers:
                fetchers.append(name)
            elif action == "remove" and name in fetchers:
                fetchers.remove(name)
            type_option[key] = fetchers

with open(dst, "w", encoding="utf-8") as f:
    json.dump(folders, f, ensure_ascii=False)

print("written", dst)
