"""Export local mission names/notes for the Korean UI audit; no dialogue or game writes."""
import argparse
import ctypes
import json
import struct
from pathlib import Path

import UnityPy

parser = argparse.ArgumentParser()
parser.add_argument("game", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
game = args.game / "Mortal_Data"
main = game / "globalgamemanagers.assets"
root = UnityPy.load(str(main))
scripts = {}
for obj in root.objects:
    if obj.type.name != "MonoScript":
        continue
    data = obj.read_typetree()
    if data.get("m_Namespace") == "Mortal.Core" and data.get("m_AssemblyName") == "Mortal.Core.dll" and data.get("m_ClassName") in ("MissionData", "MissionCheckData"):
        scripts[obj.path_id] = data["m_ClassName"]
if len(scripts) != 2:
    raise RuntimeError("Mission MonoScript identity changed")


def string_at(raw, pos):
    length = struct.unpack_from("<i", raw, pos)[0]
    if not 0 <= length <= len(raw) - pos - 4:
        raise ValueError("Unexpected mission string layout")
    end = pos + 4 + length
    return raw[pos + 4:end].decode("utf-8"), (end + 3) // 4 * 4


def simplified(text):
    if not text:
        return text
    buffer = ctypes.create_unicode_buffer(len(text) + 1)
    if ctypes.windll.kernel32.LCMapStringEx("zh-CN", 0x02000000, text, len(text), buffer, len(buffer), None, None, 0) <= 0:
        raise RuntimeError("LCMapStringEx failed")
    return buffer.value


rows = []
for path in [main, game / "resources.assets", *sorted(game.glob("sharedassets*.assets"))]:
    env = root if path == main else UnityPy.load(str(path))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        raw = obj.get_raw_data()
        if len(raw) < 32:
            continue
        file_id, script_id = struct.unpack_from("<iq", raw, 16)
        if script_id not in scripts:
            continue
        if file_id == 0 and path != main:
            continue
        if file_id > 0 and obj.assets_file.externals[file_id - 1].path.replace("\\", "/").rsplit("/", 1)[-1] != main.name:
            continue
        name, pos = string_at(raw, 28)
        key, pos = string_at(raw, pos)
        if scripts[script_id] == "MissionCheckData":
            if struct.unpack_from("<i", raw, pos)[0] not in (0, 1):
                raise ValueError("Unexpected mission active-flag layout")
            pos += 4
        desc, pos = string_at(raw, pos)
        rows.append({"type": scripts[script_id], "id": name, "name": simplified(key), "description": simplified(desc)})
if not rows:
    raise RuntimeError("No mission metadata found")
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Exported {len(rows)} mission metadata records.")
