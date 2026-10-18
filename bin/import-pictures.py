#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Imports Askwerk's background pictures (AWK-74): the owner's picks from design/AWK-74-photos/commons-candidates.json,
# CC0 photos on Wikimedia Commons. Fetches each as a 2400 px wide copy from Commons (kept in originals/, not in git),
# crops it to 16:10 (never enlarged), saves a WebP and a 480 x 300 thumbnail into the product, and writes the list
# with each picture's topic, title, creator, source and licence.
# Needs Pillow on the machine that runs it (a tool for maintainers; the product does not use it).
# Usage: bin/import-pictures.py
import json, os, sys, time, urllib.parse, urllib.request
from PIL import Image, ImageOps

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
design = os.path.join(root, "design", "AWK-74-photos")
originals = os.path.join(design, "originals")
target = os.path.join(root, "src", "backend", "Askwerk.Infrastructure", "Pictures")
UA = {"User-Agent": "AskwerkPictureImport/1.0 (https://github.com/civauva/Askwerk)"}
# The owner's picks (2026-10-18), by number on the Commons sheet.
picks = {
    "charity": [4, 5, 11, 1, 6, 13, 14, 8],
    "technology": [26, 16, 38, 28, 30, 31, 21, 22, 23, 44],
    "finance": [64, 66, 65, 55, 63],
    "holidays": [67, 77, 93, 78, 97, 80, 70, 100, 75, 99],
    "animals": [104, 113, 114, 134, 133, 135, 128, 137, 108],
}
candidates = {c["n"]: c for c in json.load(open(os.path.join(design, "commons-candidates.json"), encoding="utf-8"))}
os.makedirs(originals, exist_ok=True)
os.makedirs(target, exist_ok=True)
for old in os.listdir(target):
    if old.endswith(".webp"):
        os.remove(os.path.join(target, old))


def fetch(c, path):
    """A 2400 px wide copy from Commons (or the original when smaller)."""
    # By the Commons page id where known, else by the file's name.
    which = {"pageids": c["id"]} if c["id"].isdigit() else {"titles": "File:" + urllib.parse.unquote(c["url"].split("/")[-1])}
    query = urllib.parse.urlencode({"action": "query", **which, "prop": "imageinfo", "iiprop": "url", "iiurlwidth": 2400, "format": "json"})
    data = json.load(urllib.request.urlopen(urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + query, headers=UA), timeout=60))
    info = next(iter(data["query"]["pages"].values()))["imageinfo"][0]
    url = info.get("thumburl") or info["url"]
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as response, open(path, "wb") as file:
        file.write(response.read())


pictures = []
for topic, numbers in picks.items():
    for n in numbers:
        c = candidates[n]
        path = os.path.join(originals, f"commons-{c['id']}.img")

        if not os.path.exists(path):
            fetch(c, path)
            time.sleep(1)

        image = ImageOps.exif_transpose(Image.open(path)).convert("RGB")
        # 16:10, centred; at most 2400 px wide, never enlarged.
        width = min(2400, image.width, round(image.height * 1.6))
        cropped = ImageOps.fit(image, (width, round(width / 1.6)), method=Image.Resampling.LANCZOS)
        key = f"{topic}-{c['id']}"
        cropped.save(os.path.join(target, f"{key}.webp"), "WEBP", quality=78, method=6)
        cropped.resize((480, 300), Image.Resampling.LANCZOS).save(os.path.join(target, f"{key}.thumb.webp"), "WEBP", quality=72, method=6)
        title = c["title"].removeprefix("File:").rsplit(".", 1)[0].replace(" (Unsplash)", "").strip()
        pictures.append({"id": key, "topic": topic, "title": title, "creator": c.get("creator") or None, "license": c["license"], "source": "wikimedia", "page": c["landing"], "width": cropped.width, "height": cropped.height})
        print(f"#{n:<4} {key:<22} {cropped.width}x{cropped.height} {os.path.getsize(os.path.join(target, key + '.webp')) // 1024} KB  {title}", flush=True)

json.dump({"pictures": pictures}, open(os.path.join(target, "pictures.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print(f"{len(pictures)} imported")
