"""Re-run every offline check against an intact immutable package after a transient failure.

No previous runtime assertion is reused. Source/package hashes, test provenance and
the original successful native/publish stages must validate before any suite runs.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--node", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    from verify_release import unique_object, validate_result, NODE_SHA256, runtime_commands
    original = json.loads(args.report.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    revision = original["sourceRevision"]
    if len(revision) != 40 or any(c not in "0123456789abcdef" for c in revision):
        raise ValueError("An immutable implementation commit is required")
    work = Path(original["work"]).resolve()
    def manifest(rows, base):
        seen = set()
        for row in rows:
            relative = Path(row["path"])
            path = (base / relative).resolve()
            if relative.is_absolute() or ".." in relative.parts or not path.is_relative_to(base) or str(relative).lower() in seen:
                raise ValueError("Unsafe/duplicate manifest member")
            seen.add(str(relative).lower())
            if hashlib.sha256(path.read_bytes()).hexdigest().lower() != row["sha256"].lower():
                raise ValueError("Manifest changed: " + str(relative))
    manifest(original["sourceFiles"], work)
    manifest(original["packageFiles"], work / "app")
    if hashlib.sha256(args.node.read_bytes()).hexdigest() != NODE_SHA256:
        raise ValueError("Pinned Node runtime differs")
    projects = {"startup": "LocalAI.StartupTests", "core": "LocalAI.Tests", "extensions": "LocalAI.ExtensionTests", "files": "LocalAI.FileTests", "source": "LocalAI.SourceTests", "commands": "LocalAI.CommandTests"}
    required = {"native-backend", "publish-app", *("publish-" + label for label in projects)}
    builds = [c for c in original["checks"] if c["name"] in required]
    if len(builds) != len(required) or {c["name"] for c in builds} != required or any(c["exitCode"] != 0 for c in builds):
        raise ValueError("Every original native/publish stage must have succeeded")
    # Runtime checks use these exact previously published test binaries and the same Core.
    core_hash = hashlib.sha256((work / "app/LocalAI.Core.dll").read_bytes()).hexdigest()
    for label in projects:
        if hashlib.sha256((work / label / "LocalAI.Core.dll").read_bytes()).hexdigest() != core_hash:
            raise ValueError("Test Core differs from application Core")
    native = json.loads((work / "app/Native/backend-build.json").read_text())
    if native["sourceRevision"] != revision or hashlib.sha256((work / "app/Native/Infinity.RyzenHost.exe").read_bytes()).hexdigest() != native["sha256"]:
        raise ValueError("Native provenance mismatch")
    contracts = json.loads((work / "scripts/verification_contract.json").read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    run_root = Path(tempfile.mkdtemp(prefix="recheck-", dir=work))
    evidence = run_root / "evidence"
    for label in contracts:
        (evidence / label).mkdir(parents=True)
    (run_root / "temp").mkdir()
    env = os.environ.copy()
    for key in list(env):
        if key.upper().endswith(("API_KEY", "TOKEN")) or key.upper().startswith(("LOCALAI_TEST_", "LOCALAI_SANDBOX_")) or key.upper() in {"NODE_OPTIONS", "NODE_PATH"}:
            env.pop(key)
    env.update(TEMP=str(run_root / "temp"), TMP=str(run_root / "temp"), LOCALAI_CONVERSATIONS=str(run_root / "chats"), PYTHONDONTWRITEBYTECODE="1")
    suites = runtime_commands(work, evidence, args.node.resolve(), run_root / "temp/Infinity-user-data-fixture")
    if len(suites) != len(contracts) or {label for label, _ in suites} != set(contracts):
        raise ValueError("Unknown/missing suite contract")
    summary = {**original, "checks": builds, "passed": False, "assertions": 0, "reverificationEvidence": str(evidence), "previousReportSha256": hashlib.sha256(args.report.read_bytes()).hexdigest(), "verificationDriverSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "scope": "Prior successful native/self-contained app builds retained after complete source/package hash validation; all offline runtime assertions rerun in fresh folders. No live providers, hardware inference, physical input, UAC or MSI."}
    try:
        for label, command in suites:
            with (evidence / (label + ".log")).open("wb") as log:
                process = subprocess.Popen(command, cwd=run_root, env=env, stdout=log, stderr=subprocess.STDOUT)
                try:
                    code = process.wait(timeout=240)
                except subprocess.TimeoutExpired:
                    subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
                    process.wait(timeout=15)
                    raise RuntimeError("Owned isolated suite timed out: " + label)
            if code:
                raise RuntimeError(f"{label} failed: {code}; log {evidence / (label + '.log')}")
            result = evidence / "extension-tests.json" if label == "extensions" else evidence / label / "result.json"
            count = validate_result(result, contracts[label])
            summary["checks"].append({"name": label, "exitCode": code, "assertions": count})
            summary["assertions"] += count
        summary["passed"] = True
    finally:
        args.output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print(json.dumps({"passed": summary["passed"], "assertions": summary["assertions"], "evidence": str(evidence)}))


if __name__ == "__main__":
    main()
