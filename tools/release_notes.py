"""Extract the reviewed CHANGELOG.md entry used as the body of a tagged GitHub release."""
import argparse
from pathlib import Path
import re


def extract(changelog, version):
    if not re.fullmatch(r'\d+\.\d+\.\d+\.0', version):
        raise ValueError('Expected four-part release version with revision zero')
    sections = re.split(r'(?m)^# Kotodama ', changelog)
    matches = [section for section in sections[1:] if section.splitlines()[0].split()[0] == version]
    if len(matches) != 1:
        raise ValueError('Expected exactly one changelog entry for the release')
    notes = matches[0].partition('\n')[2].strip()
    if not notes:
        raise ValueError('Release notes are empty')
    return notes + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    notes = extract((Path(__file__).resolve().parents[1] / 'CHANGELOG.md').read_text(encoding='utf-8'), args.version)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(notes, encoding='utf-8')
