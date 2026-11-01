"""Read unencrypted asset metadata; never modify the source installation."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.tools' / 'python'))
import UnityPy

root = Path(sys.argv[1]) / 'DesktopMate_Data'
paths = list(root.glob('*.assets')) + [root / 'StreamingAssets' / 'database.info']
paths += list((root / 'StreamingAssets' / 'aa' / 'StandaloneWindows64').glob('*.bundle'))
entries = []
for path in paths:
    try:
        env = UnityPy.load(str(path))
        for obj in env.objects:
            if obj.type.name not in ('Texture2D', 'Sprite'):
                continue
            data = obj.read()
            record = dict(file=path.name, id=obj.path_id, type=obj.type.name, name=data.m_Name)
            if obj.type.name == 'Texture2D':
                record.update(width=data.m_Width, height=data.m_Height)
            entries.append(record)
    except Exception as error:
        entries.append(dict(file=path.name, error=str(error)))
out = Path(__file__).resolve().parents[1] / '.tools' / 'desktopmate-assets.json'
out.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Wrote {len(entries)} records to {out}')
for item in entries:
    if 'error' in item or 'miku' in item.get('name', '').lower():
        print(item)
