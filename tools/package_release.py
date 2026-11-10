"""Package an already built Kotodama plugin into Kotodama-X.Y.Z.0.zip.

Never builds and never writes into the input directory. CI runs it on the Release output of an
isolated runner; locally it is only ever used as a dry run on Debug output (never distribute that ZIP).
"""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NAME = 'Kotodama'
REQUIRED = (f'{NAME}.dll', f'{NAME}.json', f'{NAME}.deps.json', 'Newtonsoft.Json.dll', 'images/icon.png')
# DalamudPackager's own output (Kotodama/latest.zip) and debug symbols are not shipped.
EXCLUDED_DIRS = {NAME}
EXCLUDED_SUFFIXES = {'.pdb', '.zip'}
DOCS = ('README.md', 'CHANGELOG.md')


def version():
    return ET.parse(ROOT / 'Kotodama.Version.props').findtext('.//KotodamaVersion')


def collect(source):
    files = []
    for path in sorted(source.rglob('*')):
        relative = path.relative_to(source)
        if path.is_dir():
            continue
        if path.is_symlink():
            raise ValueError(f'Runtime files must not be symlinks: {relative}')
        if relative.parts[0] in EXCLUDED_DIRS or path.suffix.lower() in EXCLUDED_SUFFIXES:
            continue
        files.append(relative)
    return files


def package(source, destination):
    source, destination = Path(source).resolve(), Path(destination).resolve()
    if destination == source or source in destination.parents or destination in source.parents:
        raise ValueError('ZIP output must be separate from the build output')
    release = version() + '.0'
    manifest = json.loads((source / f'{NAME}.json').read_text(encoding='utf-8-sig'))
    if (manifest.get('Name'), manifest.get('InternalName'), manifest.get('AssemblyVersion')) != (NAME, NAME, release):
        raise ValueError('Manifest identity/version does not match Kotodama.Version.props')
    if not isinstance(manifest.get('DalamudApiLevel'), int):
        raise ValueError('Manifest has no DalamudApiLevel')
    missing = [name for name in REQUIRED if not (source / name).is_file()]
    if missing:
        raise ValueError('Missing runtime files: ' + ', '.join(missing))
    if (source / 'EchoXIV.dll').exists():
        raise ValueError('Upstream EchoXIV.dll must not be packaged')

    output = destination / f'{NAME}-{release}.zip'
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for relative in collect(source):
            archive.write(source / relative, relative.as_posix())
        for name in DOCS:
            archive.write(ROOT / name, name)
    return output


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path, help='Already built directory containing Kotodama.dll')
    parser.add_argument('--output', required=True, type=Path, help='Separate ZIP destination')
    args = parser.parse_args()
    print(package(args.input, args.output))
