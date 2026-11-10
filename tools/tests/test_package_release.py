import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('package_release', ROOT / 'tools' / 'package_release.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.build = Path(self.temp.name) / 'bin'
        self.out = Path(self.temp.name) / 'dist'
        self.version = module.version() + '.0'
        self.write('Kotodama.dll', b'dll')
        self.write('Kotodama.deps.json', b'{}')
        self.write('Newtonsoft.Json.dll', b'nj')
        self.write('images/icon.png', b'png')
        self.write('ja/Kotodama.resources.dll', b'res')
        self.write('Kotodama.pdb', b'pdb')
        self.write('Kotodama/latest.zip', b'packager output')
        self.manifest()

    def write(self, name, content):
        path = self.build / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def manifest(self, **overrides):
        entry = dict(Name='Kotodama', InternalName='Kotodama', AssemblyVersion=self.version, DalamudApiLevel=15)
        entry.update(overrides)
        self.write('Kotodama.json', json.dumps(entry).encode())

    def test_packages_runtime_files_satellites_and_docs_but_not_symbols(self):
        output = module.package(self.build, self.out)
        self.assertEqual(output.name, f'Kotodama-{self.version}.zip')
        names = set(zipfile.ZipFile(output).namelist())
        for expected in ('Kotodama.dll', 'Kotodama.json', 'Newtonsoft.Json.dll', 'images/icon.png',
                         'ja/Kotodama.resources.dll', 'README.md', 'CHANGELOG.md'):
            self.assertIn(expected, names)
        self.assertNotIn('Kotodama.pdb', names)
        self.assertFalse(any(n.startswith('Kotodama/') for n in names))

    def test_identity_and_version_mismatch_rejected(self):
        for overrides in (dict(InternalName='EchoXIV'), dict(AssemblyVersion='9.9.9.0'), dict(DalamudApiLevel=None)):
            self.manifest(**overrides)
            with self.assertRaises(ValueError):
                module.package(self.build, self.out)

    def test_missing_runtime_file_rejected(self):
        (self.build / 'Newtonsoft.Json.dll').unlink()
        with self.assertRaises(ValueError):
            module.package(self.build, self.out)

    def test_output_inside_build_rejected(self):
        with self.assertRaises(ValueError):
            module.package(self.build, self.build / 'dist')

    def test_existing_zip_is_never_replaced(self):
        module.package(self.build, self.out)
        with self.assertRaises(FileExistsError):
            module.package(self.build, self.out)


if __name__ == '__main__':
    unittest.main()
