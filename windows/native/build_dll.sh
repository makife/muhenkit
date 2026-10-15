#!/usr/bin/env bash
# Windows çekirdek DLL'i: mkcad (DXF/çizim) + mkcalc (hesap) → mkcore.dll (C# derlemesi MuhenKit.dll ile çakışmasın)
# Gereken: python -m pip install ziglang  (CI'da da aynı)
set -e
cd "$(dirname "$0")/../.."
OUT=windows/native/x64
mkdir -p "$OUT"
SRC="core/mkcad/src/api.cpp core/mkcad/src/reader.cpp core/mkcad/src/scene.cpp core/mkcad/src/text.cpp core/mkcad/src/writer.cpp \
     core/mkcad/src/edit.cpp core/mkcad/src/transform.cpp core/mkcad/src/offset.cpp core/mkcad/src/blocks.cpp core/mkcad/src/groups.cpp \
     core/mkcalc/src/calc.cpp core/mkcalc/src/data.cpp"
python -m ziglang c++ -std=c++17 -O2 -w -target x86_64-windows-gnu -shared \
  -Icore/mkcad/include -Icore/mkcad/src -Icore/mkcalc/include -Icore/mkcalc/src $SRC -o "$OUT/mkcore.dll"
echo "derlendi: $OUT/mkcore.dll"
