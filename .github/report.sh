#!/usr/bin/env bash
# Derleme hatalarını GitHub "annotation" olarak yayınlar (günlük indirmeden okunabilsin)
LOG="$1"
ERR=$(grep -E "error:|fatal error|Undefined symbol|ld: " "$LOG" | sed "s#$PWD/##g" | sort -u | head -40)
[ -z "$ERR" ] && ERR=$(tail -40 "$LOG")
echo "$ERR"
echo "::error title=Derleme hatasi::$(echo "$ERR" | sed ':a;N;$!ba;s/%/%25/g;s/\n/%0A/g')"
