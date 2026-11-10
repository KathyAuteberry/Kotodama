import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('release_notes', ROOT / 'tools' / 'release_notes.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReleaseNotesTests(unittest.TestCase):
    def test_extracts_only_matching_release(self):
        text = '# Kotodama 0.2.0.0 — prepared\n\nPLaMo.\n\n# Kotodama 0.1.0.0\nFirst.\n'
        self.assertEqual(module.extract(text, '0.2.0.0'), 'PLaMo.\n')

    def test_missing_duplicate_and_empty_entries_rejected(self):
        for text in ('# Kotodama 0.1.0.0\nOld', '# Kotodama 0.2.0.0\n', '# Kotodama 0.2.0.0\nOne\n# Kotodama 0.2.0.0\nTwo'):
            with self.assertRaises(ValueError):
                module.extract(text, '0.2.0.0')

    def test_invalid_version_rejected(self):
        with self.assertRaises(ValueError):
            module.extract('', '0.2.0')

    def test_current_changelog_has_an_entry_for_the_props_version(self):
        import xml.etree.ElementTree as ET
        version = ET.parse(ROOT / 'Kotodama.Version.props').findtext('.//KotodamaVersion') + '.0'
        self.assertTrue(module.extract((ROOT / 'CHANGELOG.md').read_text(encoding='utf-8'), version).strip())


if __name__ == '__main__':
    unittest.main()
