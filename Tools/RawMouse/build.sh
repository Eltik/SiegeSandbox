#!/bin/bash
# Builds the raw mouse plugin for both Apple silicon and Intel Macs.
set -e
cd "$(dirname "$0")"
OUT=../../Assets/SiegeSandbox/Plugins/macOS
mkdir -p "$OUT"
xcrun clang -O2 -Wall -dynamiclib -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -fobjc-arc -framework Foundation -framework GameController \
  -install_name @rpath/libSiegeRawMouse.dylib \
  -o "$OUT/libSiegeRawMouse.dylib" rawmouse.m
codesign --force --sign - "$OUT/libSiegeRawMouse.dylib"
lipo -info "$OUT/libSiegeRawMouse.dylib"
nm -gU "$OUT/libSiegeRawMouse.dylib" | grep siege
