#!/usr/bin/env bash
# Builds the one-click installer (Windows Forms, .NET Framework 4.5+, which ships with Windows)
# with Roslyn against the system's .NET Framework assemblies.
# Usage: DOTNET=/path/to/dotnet.exe ./Installer/build-installer.sh   -> Installer/bin/咖啡伴侣安装器 Installer.exe
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-dotnet}"
SDK_DIR="$(dirname "$(command -v "$DOTNET" || echo "$DOTNET")")/sdk"
CSC="$(find "$SDK_DIR" -path '*Roslyn/bincore/csc.dll' | sort | tail -1)"
FW="${FW:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319}"
refs=()
for n in mscorlib System System.Core System.Drawing System.Windows.Forms System.IO.Compression System.IO.Compression.FileSystem System.Management; do
	refs+=("-r:$FW/$n.dll")
done
mkdir -p bin
"$DOTNET" "$CSC" -nologo -noconfig -nostdlib+ -target:winexe -platform:anycpu -langversion:12 -optimize+ -deterministic \
	-win32manifest:app.manifest -out:"bin/咖啡伴侣安装器 Installer.exe" "${refs[@]}" \
	-resource:banner.zh.jpg,Installer.banner.zh.jpg -resource:banner.zh-Hant.jpg,Installer.banner.zh-Hant.jpg -resource:banner.en.jpg,Installer.banner.en.jpg -resource:banner.ja.jpg,Installer.banner.ja.jpg -resource:banner.ko.jpg,Installer.banner.ko.jpg -resource:../CHANGELOG.md,Installer.CHANGELOG.md -resource:../CHANGELOG.en.md,Installer.CHANGELOG.en.md -resource:../CHANGELOG.ja.md,Installer.CHANGELOG.ja.md -resource:../CHANGELOG.ko.md,Installer.CHANGELOG.ko.md *.cs ../LOMAssistant/UiTraditional.cs
echo "Built Installer/bin/咖啡伴侣安装器 Installer.exe"
