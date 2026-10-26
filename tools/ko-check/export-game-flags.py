"""Read FlagData labels from a local Unity game without launching/modifying it.

Requires UnityPy. The MonoScript identity and byte layout are validated; unexpected
layouts fail the export rather than silently reducing coverage.
"""
import argparse
import ctypes
import hashlib
import json
from pathlib import Path
import struct
import UnityPy

parser = argparse.ArgumentParser()
parser.add_argument('game', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
game = args.game / 'Mortal_Data'
main = game / 'globalgamemanagers.assets'
env = UnityPy.load(str(main))
script_ids = set()
for obj in env.objects:
    if obj.type.name == 'MonoScript':
        script = obj.read_typetree()
        if (script.get('m_ClassName'), script.get('m_Namespace'), script.get('m_AssemblyName')) == ('FlagData', 'Mortal.Core', 'Mortal.Core.dll'):
            script_ids.add(obj.path_id)
if not script_ids:
    raise RuntimeError('FlagData MonoScript not found')

def string_at(raw, offset):
    length = struct.unpack_from('<i', raw, offset)[0]
    if length < 0 or length > len(raw)-offset-4:
        raise ValueError('Invalid serialized string length')
    end = offset + 4 + length
    return raw[offset+4:end].decode('utf-8'), (end+3)//4*4

def simplified(text):
    if not text:
        return text
    buffer = ctypes.create_unicode_buffer(len(text)+1)
    written = ctypes.windll.kernel32.LCMapStringEx('zh-CN', 0x02000000, text, len(text), buffer, len(buffer), None, None, 0)
    if written <= 0:
        raise RuntimeError('LCMapStringEx failed')
    return buffer.value

flags = []
sources = {}
for path in [main, game/'resources.assets', *sorted(game.glob('sharedassets*.assets'))]:
    data = env if path == main else UnityPy.load(str(path))
    count = 0
    for obj in data.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        raw = obj.get_raw_data()
        if len(raw) < 32:
            continue
        file_id, script_id = struct.unpack_from('<iq', raw, 16)
        if script_id not in script_ids:
            continue
        if file_id == 0:
            correct_file = path == main
        else:
            externals = obj.assets_file.externals
            correct_file = file_id <= len(externals) and externals[file_id-1].path.replace('\\', '/').rsplit('/', 1)[-1] == 'globalgamemanagers.assets'
        if not correct_file:
            continue
        name, offset = string_at(raw, 28)
        note, offset = string_at(raw, offset)
        if len(raw)-offset != 8:
            raise RuntimeError(f'Unexpected FlagData layout: {path.name}/{name}: {len(raw)-offset} trailing bytes')
        flags.append({'asset': path.name, 'path_id': obj.path_id, 'id': name, 'original': note, 'simplified': simplified(note)})
        count += 1
    if count:
        sources[path.name] = {'count': count, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps({'sources': sources, 'flags': flags}, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Exported {len(flags)} FlagData objects; {sum(bool(f["original"].strip()) for f in flags)} have descriptions.')
