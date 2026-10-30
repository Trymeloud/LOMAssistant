"""Read short official terms from a locally installed game (no dialogue export)."""
import json
from pathlib import Path
import sys
import UnityPy

sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[1]
env = UnityPy.load(str(Path(sys.argv[1]) / 'Mortal_Data/sharedassets0.assets'))
tables = {}
for obj in env.objects:
    if obj.type.name != 'TextAsset':
        continue
    data = obj.read()
    name = data.m_Name
    if not name.endswith(('_kr', '_zh-cn', '_zh-tw')):
        continue
    table = {}
    for line in data.m_Script.splitlines():
        if ' = ' in line and not line.startswith('//'):
            key, value = line.split(' = ', 1)
            table[key.strip()] = value.strip()
    tables[name] = table
result = {}
for name, ko in tables.items():
    if not name.endswith('_kr'):
        continue
    for suffix in ('_zh-cn', '_zh-tw'):
        zh = tables.get(name[:-3] + suffix, {})
        for key, value in ko.items():
            # Labels only. Never bring the game's story or long descriptions into the mod.
            if key in zh and len(value) < 40 and len(zh[key]) < 35 and 'Desc' not in key and 'Intro' not in key and not any(c in value for c in '<>\\\n'):
                result.setdefault(zh[key], value)
out = root / 'bin/official-ko-terms.json'
out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print('Official short terms:', len(result))
print('Tables:', ', '.join(tables))
