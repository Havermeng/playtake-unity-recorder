"""Read-only public payload checks. Requires Python 3.10+, no Unity installation."""
from pathlib import Path
import json, re

ROOT = Path(__file__).resolve().parents[1]

def payload():
    roots = ['package.json', 'package.json.meta', 'README.md', 'README.md.meta',
             'LICENSE', 'LICENSE.meta', 'CHANGELOG.md', 'CHANGELOG.md.meta',
             'THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_NOTICES.md.meta', 'Editor.meta', 'Runtime.meta']
    paths = [ROOT / name for name in roots]
    for folder in ['Editor', 'Runtime', 'Documentation~']:
        paths += sorted(p for p in (ROOT / folder).rglob('*') if p.is_file())
    return paths

def validate():
    package = json.loads((ROOT / 'package.json').read_text(encoding='utf-8'))
    assert package['name'] == 'com.havermeng.playtake'
    assert package['version'] == '1.0.0-preview.1'
    assert package['license'] == 'MIT'
    paths = payload()
    guids = set()
    for path in paths:
        assert path.is_file(), path
        assert path.suffix.lower() not in {'.exe', '.dll', '.mp4', '.wav', '.unity', '.prefab'}, path
        text = path.read_text(encoding='utf-8-sig')
        assert not re.search(r'gh[pousr]_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{30,}|AKIA[A-Z0-9]{16}', text), path
        assert not re.search(r'[A-Za-z]:[\\/](?:Users|SecurityReview|SCPSL)', text), path
        if path.suffix == '.meta':
            guid = re.search(r'^guid: ([0-9a-f]{32})$', text, re.M)
            assert guid and guid[1] not in guids, path
            guids.add(guid[1])
        if path.suffix in {'.cs', '.asmdef'}:
            assert path.with_suffix(path.suffix + '.meta').is_file(), path
        if path.suffix == '.cs':
            assert 'namespace PlayTake' in text, path
            assert not re.search(r'\b(?:Scp939|ReferenceHub|PlayerRoles|Mirror|Steamworks|MEC)\b', text), path
    editor = json.loads((ROOT / 'Editor/PlayTake.Editor.asmdef').read_text())
    runtime = json.loads((ROOT / 'Runtime/PlayTake.Runtime.asmdef').read_text())
    assert editor['includePlatforms'] == ['Editor']
    assert editor['references'] == [runtime['name']]
    for path in (ROOT / 'Runtime').glob('*.cs'):
        text = path.read_text()
        assert text.startswith('#if UNITY_EDITOR') and text.rstrip().endswith('#endif'), path
    assert len(list(ROOT.rglob('PlayTake*.cs'))) == 3
    print(f'PASS: {len(paths)} payload files; {len(guids)} unique GUIDs; original utility sources only')
    return paths

if __name__ == '__main__':
    validate()
