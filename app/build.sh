#!/bin/bash
set -e
cd "$(dirname "$0")"
CSC=/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
OUT="$(wslpath -u "$(cd /mnt/c && cmd.exe /c 'echo %LOCALAPPDATA%' | tr -d '\r')")/G515Lighting"
mkdir -p build "$OUT"
REFS="/r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll"
"$CSC" /nologo /optimize /target:exe /out:build/selftest.exe $REFS SelfTest.cs Keyboard.cs
chmod +x build/*.exe && build/selftest.exe
"$CSC" /nologo /optimize /target:winexe /out:build/G515Lighting.exe $REFS Program.cs Keyboard.cs
taskkill.exe /im G515Lighting.exe /f >/dev/null 2>&1 || true
sleep 1
cp build/G515Lighting.exe "$OUT/"
ls -l "$OUT/G515Lighting.exe"
