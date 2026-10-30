#!/usr/bin/env bash
# 不开游戏跑测试：先编 Mod（./build.sh），再把 tests/ 编成一个小程序，用 Windows 自带的 .NET Framework 跑。
# 用法：DOTNET=/path/to/dotnet.exe ./tests/run-tests.sh      （GAME= 可指定游戏目录）
set -euo pipefail
cd "$(dirname "$0")/.."

GAME="${GAME:-D:/Steam/steamapps/common/LegendOfMortal}"
DOTNET="${DOTNET:-dotnet}"
SDK_DIR="$(dirname "$(command -v "$DOTNET" || echo "$DOTNET")")/sdk"
CSC="$(find "$SDK_DIR" -path '*Roslyn/bincore/csc.dll' | sort | tail -1)"
FW=/c/Windows/Microsoft.NET/Framework64/v4.0.30319
M="$GAME/Mortal_Data/Managed"

if [ ! -f bin/LOMAssistant.dll ]; then
	echo "先跑 ./build.sh 编出 bin/LOMAssistant.dll" >&2
	exit 1
fi
mkdir -p bin/tests
cp bin/LOMAssistant.dll bin/tests/
"$DOTNET" "$CSC" -nologo -noconfig -nostdlib+ -target:exe -platform:x64 -langversion:12 -optimize+ \
	-out:bin/tests/LOMAssistant.Tests.exe \
	-r:$FW/mscorlib.dll -r:$FW/System.dll -r:$FW/System.Core.dll -r:"$M/netstandard.dll" \
	-r:bin/LOMAssistant.dll -r:"$M/UnityEngine.CoreModule.dll" -r:"$GAME/BepInEx/core/BepInEx.Core.dll" \
	-r:"$GAME/BepInEx/plugins/LOMAssistant/Newtonsoft.Json.dll" \
	tests/*.cs
./bin/tests/LOMAssistant.Tests.exe "$GAME"
