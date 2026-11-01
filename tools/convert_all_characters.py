"""Convert and validate every local CB character; install only validated results."""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(script, *args):
    result = subprocess.run(
        [sys.executable, str(ROOT / 'tools' / script), *map(str, args)],
        capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Desktop Mate AssetBundle directory')
    parser.add_argument('--output', type=Path, default=ROOT / 'assets' / 'local')
    parser.add_argument('--install', type=Path, help='Mod Characters directory')
    args = parser.parse_args()
    sources = sorted(args.source.glob('*.cb'), key=lambda p: p.name.lower())
    if not sources:
        parser.error('No .cb files found')
    args.output.mkdir(parents=True, exist_ok=True)
    report_path = args.output / 'conversion-report.json'
    report = dict(source=str(args.source), total=len(sources), characters=[])
    for index, source in enumerate(sources, 1):
        started = time.monotonic()
        character_id = source.stem.lower() + '-local'
        record = dict(id=character_id, source=source.name)
        print(f'[{index}/{len(sources)}] {source.name}', flush=True)
        try:
            # Never expose an incomplete character to the game or replace a good
            # export with an invalid one. Temporary data stays inside .tools.
            with tempfile.TemporaryDirectory(prefix='character-', dir=ROOT / '.tools') as temporary:
                temp = Path(temporary)
                bundle = temp / 'decoded.unity3d'
                output = temp / character_id
                run('read_character_bundle.py', source, bundle, '--decode-only')
                run('export_local_character.py', bundle, source, output)
                validation = run('validate_portable_character.py', output / 'character.dmc')
                metadata_path = output / 'source.json'
                metadata = json.loads(metadata_path.read_text(encoding='utf-8'))
                metadata['source_sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
                metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
                destination = args.output / character_id
                shutil.copytree(output, destination, dirs_exist_ok=True)
                if args.install:
                    installed = args.install / character_id
                    shutil.copytree(output, installed, dirs_exist_ok=True)
                    run('validate_portable_character.py', installed / 'character.dmc')
                record.update(status='pass', validation=validation,
                              output=str(destination), installed=str(args.install / character_id) if args.install else None,
                              bytes=sum(p.stat().st_size for p in output.iterdir() if p.is_file()))
                print('  ' + validation, flush=True)
        except Exception as error:
            record.update(status='failed', error=str(error))
            print('  FAILED: ' + str(error)[-2000:], flush=True)
        record['seconds'] = round(time.monotonic() - started, 2)
        report['characters'].append(record)
        report['passed'] = sum(r['status'] == 'pass' for r in report['characters'])
        report['failed'] = sum(r['status'] == 'failed' for r in report['characters'])
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Finished: {report['passed']}/{len(sources)} passed, {report['failed']} failed. {report_path}", flush=True)
    catalog = [
        '# 本机 Desktop Mate 角色转换清单', '',
        f"转换并通过检查：{report['passed']}/{len(sources)}；失败：{report['failed']}。", '',
        '保留三维网格、骨骼、表情形变和主贴图，导出默认启用的网格。',
        '材质为简化近似；不含原版动画控制器、着色器、布料/头发物理。',
        '文件检查不代替游戏内逐个查看；动作效果取决于角色骨骼与当前驱动的匹配情况。', '',
        '在游戏 Mods/DesktopMate/DesktopMate.toml 的 [Display] 下设置 Renderer = "3D"，',
        '在 [Character] 下把 Id 改为表中角色 ID，重启游戏生效。', '',
        '| 原始角色包 | 角色 ID | 文件检查 | 大小 MiB |',
        '| --- | --- | --- | ---: |',
    ]
    for record in report['characters']:
        catalog.append(f"| {record['source']} | `{record['id']}` | {record['status']} | {record.get('bytes', 0) / 1048576:.2f} |")
    (args.output / 'CHARACTERS.md').write_text('\n'.join(catalog) + '\n', encoding='utf-8')
    return 1 if report['failed'] else 0


if __name__ == '__main__':
    sys.exit(main())
