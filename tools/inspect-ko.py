from pathlib import Path
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[1]
if sys.argv[1] == 'rows':
    rows = re.findall(r'new\[\] \{ "((?:[^"\\]|\\.)*)"', (root / 'LOMAssistant' / sys.argv[2]).read_text(encoding='utf-8'))
    start = int(sys.argv[3]) if len(sys.argv) > 3 else 0
    end = int(sys.argv[4]) if len(sys.argv) > 4 else len(rows)
    for i in range(start, min(end, len(rows))):
        print(f'{i+1}\t{rows[i]}')
elif sys.argv[1] == 'assets':
    import UnityPy
    p = Path(sys.argv[2])
    env = UnityPy.load(str(p))
    for obj in env.objects:
        if obj.type.name not in ('MonoBehaviour', 'TextAsset'):
            continue
        try:
            data = obj.read_typetree()
            name = data.get('m_Name', '')
            if any(k in str(data) for k in ('Korean', '嘴力', '口才', '말재주', '말솜씨')):
                out = root / 'bin' / 'game-inspect'
                out.mkdir(parents=True, exist_ok=True)
                import json
                (out / f'{p.stem}_{obj.path_id}.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
                print(obj.path_id, name, list(data)[:15])
        except Exception as e:
            print(type(e).__name__, str(e)[:120])
