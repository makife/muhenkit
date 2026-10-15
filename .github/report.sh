#!/usr/bin/env bash
# Derleme hatalarını GitHub "annotation" olarak yayınlar (günlük indirmeden okunabilsin)
LOG="$1"
python3 - "$LOG" "$PWD" <<'PY'
import sys, re
log, cwd = sys.argv[1], sys.argv[2]
lines = open(log, errors="replace").read().splitlines()
keep = []
for i, l in enumerate(lines):
    if re.search(r"error:|fatal error|Undefined symbol|ld: |\*\* BUILD FAILED|HATA", l):
        s = l.replace(cwd + "/", "").strip()
        if s not in keep:
            keep.append(s)
if not keep:
    keep = lines[-40:]
msg = "\n".join(keep[:40])
print(msg)
print("::error title=Derleme hatasi::" + msg.replace("%", "%25").replace("\r", "").replace("\n", "%0A"))
PY
