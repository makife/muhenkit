#!/usr/bin/env bash
# Derleme hatalarını GitHub "annotation" olarak yayınlar (günlük indirmeden okunabilsin)
LOG="$1"
python3 - "$LOG" "$PWD" <<'PY'
import sys, re
log, cwd = sys.argv[1], sys.argv[2]
lines = open(log, errors="replace").read().splitlines()
keep = []
def add(s):
    s = s.replace(cwd + "/", "").strip()
    if s and s not in keep:
        keep.append(s)
for i, l in enumerate(lines):
    if re.search(r"error:|fatal error|Undefined symbol|ld: |HATA|unable to|cannot |No such file", l):
        add(l)
# "The following build commands failed:" bloğu
for i, l in enumerate(lines):
    if "The following build commands failed" in l:
        for x in lines[i:i + 8]:
            add(x)
if len(keep) < 3:
    for x in lines[-30:]:
        add(x)
msg = "\n".join(keep[:45])
print(msg)
print("::error title=Derleme hatasi::" + msg.replace("%", "%25").replace("\r", "").replace("\n", "%0A"))
PY
