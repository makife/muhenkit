#!/usr/bin/env bash
# Masaüstü (Windows x64) derlemesi: test ve flutter test için mkcad.dll + mkcad_cli.exe
# Gereken: python -m pip install ziglang==0.13.0
set -e
cd "$(dirname "$0")"
mkdir -p build_host
SRC="src/api.cpp src/reader.cpp src/scene.cpp src/text.cpp src/writer.cpp src/edit.cpp src/transform.cpp src/offset.cpp src/blocks.cpp src/groups.cpp"
python -m ziglang c++ -std=c++17 -O2 -w -target x86_64-windows-gnu -Iinclude -Isrc -shared $SRC -o build_host/mkcad.dll
python -m ziglang c++ -std=c++17 -O2 -w -target x86_64-windows-gnu -Iinclude -Isrc tools/mkcad_cli.cpp $SRC -o build_host/mkcad_cli.exe
python -m ziglang c++ -std=c++17 -O2 -w -target x86_64-windows-gnu -Iinclude -Isrc tools/mkcad_edit_test.cpp $SRC -o build_host/mkcad_edit_test.exe
echo "derlendi: build_host/"
