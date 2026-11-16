#!/bin/bash
# Download the files of a free itch.io asset pack through the site's normal "Download" flow.
#   Tools/itch_download.sh <page url> <output folder> [only files whose name matches this pattern]
set -e
P="$1"; OUT="$2"; ONLY="${3:-.}"; UA='Mozilla/5.0'
mkdir -p "$OUT"; J="$OUT/.cookies"; rm -f "$J"
curl -s -c "$J" -A "$UA" "$P" -o "$OUT/.page.html"
T=$(grep -o 'csrf_token" value="[^"]*' "$OUT/.page.html" | head -1 | sed 's/.*value="//')
U=$(curl -s -b "$J" -c "$J" -A "$UA" -X POST --data-urlencode "csrf_token=$T" "$P/download_url" | python3 -c 'import json,sys;print(json.load(sys.stdin)["url"])')
curl -s -b "$J" -c "$J" -A "$UA" "$U" -o "$OUT/.dl.html"
python3 - "$OUT/.dl.html" > "$OUT/.uploads.txt" <<'PY'
import re,sys,html
s=open(sys.argv[1],encoding='utf-8',errors='ignore').read()
# Each upload is a block holding its id and its file name, in either order.
for block in re.split(r'(?=<div[^>]*class="upload")', s):
    i=re.search(r'data-upload_id="(\d+)"', block); n=re.search(r'class="name" title="([^"]*)"', block)
    if i and n: print(i.group(1)+"\t"+html.unescape(n.group(1)))
PY
cat "$OUT/.uploads.txt"
while IFS=$'\t' read -r ID NAME; do
  echo "$NAME" | grep -qi "$ONLY" || continue
  F=$(curl -s -b "$J" -c "$J" -A "$UA" -e "$P" -X POST --data-urlencode "csrf_token=$T" "$P/file/$ID?source=view_game&as_props=1&after_download_lightbox=true" | python3 -c 'import json,sys;print(json.load(sys.stdin)["url"])')
  curl -sL -o "$OUT/$NAME" "$F"
  ls -la "$OUT/$NAME"
done < "$OUT/.uploads.txt"
rm -f "$J" "$OUT/.page.html" "$OUT/.uploads.txt"; [ -n "$KEEP_PAGE" ] || rm -f "$OUT/.dl.html"
