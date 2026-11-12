import os
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
import uuid
from cleanup_ci import cleanup


class CleanupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Infinity-cleanup-check-")
        self.base = Path(self.temp.name)
        self.root = self.base / ("Infinity-ci-" + uuid.uuid4().hex)
        self.root.mkdir()

    def tearDown(self):
        if self.root.exists():
            cleanup(self.root, self.base)
        self.temp.cleanup()

    def test_read_only_git_objects_removed(self):
        path = self.root / "objects" / "example"
        path.parent.mkdir()
        path.write_text("synthetic Git object")
        os.chmod(path, stat.S_IREAD)
        cleanup(self.root, self.base)
        self.assertFalse(self.root.exists())

    def test_other_directory_refused(self):
        with self.assertRaises(ValueError):
            cleanup(self.base, self.base)
        self.assertTrue(self.root.exists())

    def test_missing_owned_directory_is_idempotent(self):
        self.root.rmdir()
        cleanup(self.root, self.base)

    def test_read_only_hardlinks_inside_owned_root_removed(self):
        first = self.root / "first"
        first.write_text("synthetic object")
        os.link(first, self.root / "second")
        os.chmod(first, stat.S_IREAD)
        cleanup(self.root, self.base)
        self.assertFalse(self.root.exists())

    @unittest.skipUnless(os.name == "nt", "Windows attributes")
    def test_read_only_external_hardlink_attributes_preserved(self):
        outside = self.base / "original"
        outside.write_text("preserve")
        os.link(outside, self.root / "shared")
        os.chmod(outside, stat.S_IREAD)
        try:
            with self.assertRaises(PermissionError):
                cleanup(self.root, self.base)
            self.assertTrue(outside.stat().st_file_attributes & 1)
            self.assertEqual(outside.read_text(), "preserve")
        finally:
            os.chmod(outside, stat.S_IREAD | stat.S_IWRITE)

    @unittest.skipUnless(os.name == "nt", "Windows junction")
    def test_junction_target_remains_untouched(self):
        outside = self.base / "outside"
        outside.mkdir()
        sentinel = outside / "keep.txt"
        sentinel.write_text("preserve")
        link = self.root / "linked"
        subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(outside)], check=True, capture_output=True)
        cleanup(self.root, self.base)
        self.assertEqual(sentinel.read_text(), "preserve")


if __name__ == "__main__":
    unittest.main()
