"""Build reproducible UPM tarball and Unitypackage; verify every archive member."""
from pathlib import Path
import gzip, hashlib, io, json, re, tarfile, uuid
from validate import ROOT, validate

VERSION = '1.0.0-preview.1'
DIST = ROOT / 'dist'

def write_tar(path, entries):
    with path.open('wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode='w', format=tarfile.USTAR_FORMAT) as tar:
            for name, data in sorted(entries.items()):
                info = tarfile.TarInfo(name)
                info.size = len(data)
                info.mode = 0o644
                info.mtime = info.uid = info.gid = 0
                info.uname = info.gname = ''
                tar.addfile(info, io.BytesIO(data))
    with tarfile.open(path, 'r:gz') as tar:
        members = tar.getmembers()
        assert len(members) == len(entries)
        for member in members:
            assert member.isfile() and '..' not in Path(member.name).parts
            assert tar.extractfile(member).read() == entries[member.name]

def main():
    paths = validate()
    DIST.mkdir(exist_ok=True)
    upm = {'package/' + p.relative_to(ROOT).as_posix(): p.read_bytes() for p in paths}
    tgz = DIST / f'com.havermeng.playtake-{VERSION}.tgz'
    write_tar(tgz, upm)
    entries = {}
    root_guid = uuid.uuid5(uuid.NAMESPACE_URL, 'com.havermeng.playtake/Assets/PlayTake').hex
    root_meta = f'fileFormatVersion: 2\nguid: {root_guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n'.encode()
    entries[root_guid + '/asset.meta'] = root_meta
    entries[root_guid + '/pathname'] = b'Assets/PlayTake'
    for p in paths:
        rel = p.relative_to(ROOT).as_posix()
        if p.suffix == '.meta' or rel.startswith('Documentation~') or rel.startswith('package.json'):
            continue
        meta = p.with_suffix(p.suffix + '.meta').read_bytes()
        guid = re.search(rb'^guid: ([0-9a-f]{32})\r?$', meta, re.M)[1].decode()
        entries[guid + '/asset'] = p.read_bytes()
        entries[guid + '/asset.meta'] = meta
        entries[guid + '/pathname'] = ('Assets/PlayTake/' + rel).encode()
    for folder in ['Editor', 'Runtime']:
        meta = (ROOT / (folder + '.meta')).read_bytes()
        guid = re.search(rb'^guid: ([0-9a-f]{32})\r?$', meta, re.M)[1].decode()
        entries[guid + '/asset.meta'] = meta
        entries[guid + '/pathname'] = ('Assets/PlayTake/' + folder).encode()
    unitypackage = DIST / f'PlayTake-{VERSION}.unitypackage'
    write_tar(unitypackage, entries)
    result = {p.name: {'bytes': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in [tgz, unitypackage]}
    (DIST / 'SHA256SUMS.txt').write_text(''.join(f'{v["sha256"]}  {k}\n' for k, v in result.items()))
    (DIST / 'release-manifest.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
