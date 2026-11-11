"""Adversarial validation of the release gate; no network or provider calls."""
import json
import subprocess
from pathlib import Path
import tempfile
import unittest
from verify_release import export_snapshot, validate_result, runtime_commands


class ResultContractTests(unittest.TestCase):
    def test_release_and_recheck_cover_every_contract_exactly_once(self):
        contract = json.loads((Path(__file__).parent / "verification_contract.json").read_text(encoding="utf-8-sig"))
        root = Path(tempfile.gettempdir()) / "suite-list-only"
        commands = runtime_commands(root, root / "evidence", root / "node.exe", root / "data-fixture")
        self.assertEqual(len(commands), len(contract))
        self.assertEqual({name for name, _ in commands}, set(contract))

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Infinity-gate-test-")
        self.path = Path(self.temp.name) / "result.json"

    def tearDown(self):
        self.temp.cleanup()

    def validate(self, value, expected=("one", "two")):
        self.path.write_text(json.dumps(value), encoding="utf-8")
        return validate_result(self.path, list(expected))

    def test_valid_results(self):
        self.assertEqual(self.validate({"one": True, "two": True}), 2)

    def test_fail_closed(self):
        for value in ({}, [], {"one": True}, {"one": True, "two": False}, {"one": True, "two": "true"}, {"one": True, "two": True, "error": "bad"}, {"one": True, "two": True, "researchUiError": "bad"}, [{"name": "one", "passed": True, "error": "bad"}, {"name": "two", "passed": True}], [{"name": "one", "passed": True}, {"name": "one", "passed": True}]):
            with self.subTest(value=value), self.assertRaises((ValueError, KeyError)):
                self.validate(value)

    def test_duplicate_json_keys_rejected(self):
        self.path.write_text('{"one":false,"one":true,"two":true}', encoding="utf-8")
        with self.assertRaises(ValueError):
            validate_result(self.path, ["one", "two"])

    def test_duplicate_expected_keys_rejected(self):
        with self.assertRaises(ValueError):
            self.validate({"one": True}, ("one", "one"))


class ImmutableSnapshotTests(unittest.TestCase):
    def exercise_moving_source(self, requested):
        with tempfile.TemporaryDirectory(prefix="Infinity-git-gate-") as directory:
            root = Path(directory)
            repo = root / "repo"
            repo.mkdir()
            hooks = root / "empty-hooks"
            hooks.mkdir()

            def git(*args):
                return subprocess.check_output(["git", "-c", "core.hooksPath=" + str(hooks), "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", "-c", "user.name=Infinity QA", "-c", "user.email=qa@example.invalid", *args], cwd=repo, stderr=subprocess.DEVNULL)

            git("init", "-q")
            (repo / "source.txt").write_bytes(b"original source\n")
            (repo / "appsettings.json").write_bytes(b'{"fixture":"excluded configuration"}')
            git("add", ".")
            git("commit", "-qm", "original")
            original = git("write-tree" if requested == "INDEX" else "rev-parse", *([] if requested == "INDEX" else ["HEAD"])).decode().strip()
            moved = False

            def moving_git(*args):
                nonlocal moved
                if args[0] == "ls-tree" and not moved:
                    moved = True
                    (repo / "source.txt").write_bytes(b"later source\n")
                    (repo / "later.txt").write_bytes(b"added during export\n")
                    git("add", ".")
                    if requested != "INDEX":
                        git("commit", "-qm", "later")
                return git(*args)

            exported = root / "export"
            revision, files = export_snapshot(moving_git, requested, exported)
            self.assertTrue(moved)
            self.assertEqual(revision, ("index-" if requested == "INDEX" else "") + original)
            self.assertEqual((exported / "source.txt").read_bytes(), b"original source\n")
            self.assertFalse((exported / "later.txt").exists())
            self.assertFalse((exported / "appsettings.json").exists())
            self.assertEqual([record["path"] for record in files], ["source.txt"])

    def test_head_movement_cannot_change_export(self):
        self.exercise_moving_source("HEAD")

    def test_index_movement_cannot_change_export(self):
        self.exercise_moving_source("INDEX")


if __name__ == "__main__":
    unittest.main()
