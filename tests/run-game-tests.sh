#!/usr/bin/env bash
# 游戏内自检：用真实的游戏数据测剧情跳转、选项后果、剧本解析、AI 查资料等（见 LOMAssistant/SelfTest.cs）。
# 先 ./build.sh 装好 Mod；这个脚本带着 LOMASSISTANT_SELFTEST 启动游戏，先在标题画面跑只读的检查，
# 再把一份第 2 年 1 月的手动存档复制到空栏位（100 往下第一个空的）读进去测，测完删掉那一栏，游戏自己退出，再打印报告。
# 存档保护：启动前把整个存档文件夹备份到 bin/ 下、记下每个文件的指纹；退出后用备份把存档文件夹还原（读档、跳转写的随时存档、
# 剧情记录、自动存档都撤掉），再逐个比对指纹，一模一样才删备份，不一样就报出来、留着备份。
# 要开着 Steam；SteamAppId 让游戏不经过 Steam 重启（重启会丢掉环境变量）。
# 用法：./tests/run-game-tests.sh      （GAME= 可指定游戏目录）
set -euo pipefail
cd "$(dirname "$0")/.."

GAME="${GAME:-D:/Steam/steamapps/common/LegendOfMortal}"
SAVES="${SAVES:-$USERPROFILE/AppData/LocalLow/Obb Studio/Mortal}"
SAVES="$(cygpath -u "$SAVES")"
REPORT="$(pwd)/bin/selftest-report.txt"
STAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP="$(pwd)/bin/save-backup-$STAMP"
mkdir -p bin
rm -f "$REPORT"

if tasklist //FI "IMAGENAME eq Mortal.exe" 2>/dev/null | grep -qi mortal.exe; then
	echo "游戏正在运行，先关掉再测" >&2
	exit 1
fi

# 存档指纹（日志文件不算，游戏每次启动都会重写）
fingerprint() {
	(cd "$SAVES" && find . -type f ! -name 'Player*.log' -print0 | sort -z | xargs -0 md5sum)
}
if [ -d "$SAVES" ]; then
	# 用 Windows 自带的 robocopy 复制（Git 的 cp 批量复制存档会被卡巴斯基当成勒索软件杀掉，备份就不完整了）
	# robocopy 返回 8 以下都是成功
	robocopy "$(cygpath -w "$SAVES")" "$(cygpath -w "$BACKUP")" //E //NFL //NDL //NJH //NJS //NP >/dev/null || [ $? -lt 8 ]
	fingerprint > "bin/save-before-$STAMP.txt"
	# 备份要和存档一模一样，不然不启动游戏
	if [ "$(cd "$BACKUP" && find . -type f ! -name 'Player*.log' -print0 | sort -z | xargs -0 md5sum)" != "$(cat "bin/save-before-$STAMP.txt")" ]; then
		echo "存档备份不完整（可能被杀毒软件拦了），不启动游戏。备份在 $BACKUP" >&2
		exit 1
	fi
	echo "存档已备份到 $BACKUP，核对过一模一样"
fi

export LOMASSISTANT_SELFTEST="$(cygpath -w "$REPORT")"
export SteamAppId=1859910
export SteamGameId=1859910
(cd "$GAME" && ./Mortal.exe -screen-width 960 -screen-height 540 -screen-fullscreen 0 &)

for i in $(seq 1 300); do
	if [ -s "$REPORT" ] && ! tasklist //FI "IMAGENAME eq Mortal.exe" 2>/dev/null | grep -qi mortal.exe; then
		break
	fi
	sleep 2
done
if [ ! -s "$REPORT" ]; then
	echo "10 分钟内没拿到报告，看游戏目录 BepInEx/LogOutput.log" >&2
fi
# 游戏还没退干净就等一会，再不退就关掉，免得还原时它还在写
for i in $(seq 1 15); do
	tasklist //FI "IMAGENAME eq Mortal.exe" 2>/dev/null | grep -qi mortal.exe || break
	sleep 2
done
taskkill //IM Mortal.exe //F >/dev/null 2>&1 || true

status=0
if [ -d "$SAVES" ]; then
	sleep 2
	# 用备份还原：多出来的文件删掉、改过的换回去（日志不动）
	robocopy "$(cygpath -w "$BACKUP")" "$(cygpath -w "$SAVES")" //MIR //XF "Player*.log" //NFL //NDL //NJH //NJS //NP >/dev/null || [ $? -lt 8 ]
	fingerprint > "bin/save-after-$STAMP.txt"
	if diff -q "bin/save-before-$STAMP.txt" "bin/save-after-$STAMP.txt" >/dev/null; then
		echo "存档已还原，和测之前一模一样"
		rm -rf "$BACKUP" "bin/save-before-$STAMP.txt" "bin/save-after-$STAMP.txt"
	else
		echo "！！存档没能还原成测之前的样子，备份在 $BACKUP：" >&2
		diff "bin/save-before-$STAMP.txt" "bin/save-after-$STAMP.txt" >&2 || true
		status=1
	fi
fi
[ -s "$REPORT" ] || exit 1
cat "$REPORT"
grep -q "，0 项失败" "$REPORT" || status=1
exit $status
