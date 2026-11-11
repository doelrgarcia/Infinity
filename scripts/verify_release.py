"""Verify an exact Git snapshot with isolated offline, exact-key tests.

Requires Windows, PowerShell 7, .NET 10 SDK and an explicitly supplied pinned public Node runtime.
No authenticated provider, installed desktop assistant, model or API key is required.
Use --ref INDEX for staged candidates; releases must use a commit reference.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import shutil
import tempfile
import sys

NODE_SHA256 = "d702daa815eabbff763c7d3808e235cf031cf2ca70211b56847bc72f51e240e5"


UI_SUITES = ("tools", "setup", "workspace", "escalation", "chat-files", "chat-commands",
             "project", "agent-features", "agent-ui", "agent", "chat-assistant", "debug",
             "browser", "app-controls", "voice-ui", "commands-ui", "source-ui", "files-ui",
             "picker-ui", "ui-paths", "ui-dialogs", "ui-files", "ui-auxiliary", "ui-agent-paths", "drop-ui", "copilot")


def runtime_commands(work, evidence, node, user_data_fixture, ui_args=None):
    """One shared suite list prevents release and recheck coverage from drifting."""
    if ui_args is None:
        ui_args = lambda name: ["dotnet", str(work / "app/LocalAI.Desktop.dll"), "--" + name + "-qa", str(evidence / name / "result.json")]
    return [
        ("installer", ["pwsh", "-NoProfile", "-NonInteractive", "-File", str(work / "scripts/test_install_local_release.ps1"), "-ApplicationRoot", str(work / "app"), "-ResultPath", str(evidence / "installer/result.json")]),
        ("user-data", ["dotnet", str(work / "data-artifacts/bin/UserDataQa/release/UserDataQa.dll"), str(user_data_fixture), str(evidence / "user-data/result.json")]),
        ("startup", ["dotnet", str(work / "startup/LocalAI.StartupTests.dll"), str(evidence / "startup/result.json")]),
        ("cursor-stdin", ["dotnet", str(work / "startup/LocalAI.StartupTests.dll"), "--cursor-stdin-qa", str(evidence / "cursor-stdin/result.json"), str(node)]),
        ("core", ["dotnet", str(work / "core/LocalAI.Tests.dll"), str(evidence / "core/result.json"), "--offline"]),
        ("extensions", ["dotnet", str(work / "extensions/LocalAI.ExtensionTests.dll")]),
        ("files", ["dotnet", str(work / "files/LocalAI.FileTests.dll"), str(evidence / "files/result.json")]),
        ("commands", ["dotnet", str(work / "commands/LocalAI.CommandTests.dll"), str(evidence / "commands/result.json")]),
        ("source", ["dotnet", str(work / "source/LocalAI.SourceTests.dll"), str(evidence / "source/result.json")]),
        ("deepseek-bridge", [str(node), str(work / "scripts/test_deepseek_bridge.cjs"), str(evidence / "deepseek-bridge/result.json")]),
        *[(name, ui_args(name)) for name in UI_SUITES],
    ]


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key: " + key)
        result[key] = value
    return result


def validate_result(path, expected):
    if not expected or len(expected) != len(set(expected)):
        raise ValueError("Invalid expected assertion contract")
    value = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    if isinstance(value, list):
        if any(not isinstance(row, dict) or set(row) != {"name", "passed"} or not isinstance(row["name"], str) for row in value):
            raise ValueError("Unexpected assertion row fields")
        names = [row["name"] for row in value]
        passed = all(row.get("passed") is True for row in value)
    elif isinstance(value, dict):
        names = [key for key, item in value.items() if type(item) is bool]
        passed = all(value[key] is True for key in names) and not (set(value) - set(names) - {"scratch"})
    else:
        raise ValueError("Unexpected result format")
    if not names or len(names) != len(set(names)) or sorted(names) != sorted(expected) or not passed:
        raise ValueError(f"Contract failed: {path.name}; expected {len(expected)} unique passing keys, got {len(names)}")
    return len(names)


def export_snapshot(git, requested, work):
    # Resolve mutable HEAD/index once. Every tree/blob read then uses this object.
    staged = requested == "INDEX"
    reference = git("write-tree").decode().strip() if staged else git("rev-parse", requested + "^{commit}").decode().strip()
    if len(reference) != 40 or any(character not in "0123456789abcdef" for character in reference):
        raise ValueError("Expected an immutable SHA-1 Git object")
    revision = "index-" + reference if staged else reference
    names = git("ls-tree", "-rz", "--name-only", reference)
    files = []
    for name in filter(None, names.decode().split("\0")):
        relative = Path(name)
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError("Unsafe source path")
        if name.startswith("evidence/") or relative.name.lower().startswith("appsettings"):
            continue
        data = git("show", reference + ":" + name)
        if relative.suffix.lower() in {".cs", ".csproj", ".py", ".ps1", ".json", ".md", ".txt"}:
            data.decode("utf-8-sig")
        target = work / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        files.append({"path": name, "sha256": hashlib.sha256(data).hexdigest()})
    return revision, files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="HEAD")
    parser.add_argument("--node", required=True, type=Path)
    parser.add_argument("--native-inputs", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    options = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    node = options.node.resolve()
    if hashlib.sha256(node.read_bytes()).hexdigest() != NODE_SHA256:
        raise ValueError("Node fixture hash differs from the pinned public runtime")
    git = lambda *args: subprocess.check_output(["git", *args], cwd=repo)
    work = Path(tempfile.mkdtemp(prefix="Infinity-verify-"))
    evidence = work / "evidence"
    evidence.mkdir()
    for suite in ("installer", "user-data", "deepseek-bridge", "startup", "cursor-stdin", "core", "tools", "setup", "workspace", "escalation", "files", "files-ui", "source", "source-ui", "commands", "commands-ui", "voice-ui", "app-controls", "browser", "debug", "chat-assistant", "agent", "agent-features", "agent-ui", "chat-files", "chat-commands", "project", "picker-ui", "ui-paths", "ui-dialogs", "ui-files", "ui-auxiliary", "ui-agent-paths", "drop-ui", "copilot"):
        (evidence / suite).mkdir()
    (work / "temp").mkdir()
    revision, files = export_snapshot(git, options.ref, work)
    contracts = json.loads((work / "scripts/verification_contract.json").read_text(encoding="utf-8-sig"))
    settings = {"Models": [{"Id": profile, "Label": profile, "Engine": engine, "Executable": "qa-missing.exe", "ModelPath": "qa-missing.gguf", "Endpoint": "http://127.0.0.1:62341", "ContextLimit": 8192} for profile, engine in [("qwen25-coder", "llama"), ("qwen3-coder", "llama"), ("llama32-hybrid", "ryzen"), ("cloud-codex", "codex"), ("cloud-cursor", "cursor"), ("cloud-grok", "grok")]]}
    # AppPaths resolves UserDataRoot from the host assembly's configuration.
    # LOCALAI_CONVERSATIONS alone does not isolate the Agent and normal chat stores.
    (work / "user-data").mkdir()
    settings["UserDataRoot"] = str(work / "user-data")
    (work / "appsettings.json").write_text(json.dumps(settings), encoding="utf-8")
    env = os.environ.copy()
    for key in list(env):
        if key.upper().endswith(("API_KEY", "TOKEN")) or key.upper().startswith(("LOCALAI_TEST_", "LOCALAI_SANDBOX_")) or key.upper() in {"NODE_OPTIONS", "NODE_PATH"}:
            env.pop(key)
    env.update(TEMP=str(work / "temp"), TMP=str(work / "temp"), LOCALAI_CONVERSATIONS=str(work / "chats"), UseSharedCompilation="false", MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")
    checks = []

    def run(label, args, timeout=240, environment=None):
        with (evidence / (label + ".log")).open("wb") as log:
            process = subprocess.Popen(args, cwd=work, env=env if environment is None else environment, stdout=log, stderr=subprocess.STDOUT)
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
                process.wait(timeout=15)
                raise RuntimeError("Owned isolated test timed out: " + label)
        if code:
            raise RuntimeError(f"{label} failed with exit {code}; log: {evidence / (label + '.log')}")
        checks.append({"name": label, "exitCode": code})

    summary = {"sourceRevision": revision, "sourceFiles": files, "work": str(work), "checks": checks, "passed": False, "scope": "offline fixture/control-handler verification; no live providers, hardware inference, physical input, native MSI or UAC"}
    try:
        run("native-backend", [sys.executable, str(work / "native/build_backend.py"), "--inputs", str(options.native_inputs.resolve()), "--output", str(work / "native-output"), "--revision", revision])
        for project, output in [("LocalAI.Desktop", "app"), ("LocalAI.StartupTests", "startup"), ("LocalAI.Tests", "core"), ("LocalAI.ExtensionTests", "extensions"), ("LocalAI.FileTests", "files"), ("LocalAI.SourceTests", "source"), ("LocalAI.CommandTests", "commands")]:
            run("publish-" + output, ["dotnet", "publish", project, "-c", "Release", "-r", "win-x64", "--self-contained", "true" if output == "app" else "false", "-o", str(work / output), "-p:InfinityNativeBackendDirectory=" + str(work / "native-output"), "-p:SourceRevisionId=" + revision, "-p:InformationalVersion=1.0.0+" + revision, "-p:IncludeSourceRevisionInInformationalVersion=false", "--nologo"])
        summary["packageFiles"] = [{"path": str(path.relative_to(work / "app")), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in sorted((work / "app").rglob("*")) if path.is_file() and path.name.lower() != "appsettings.json"]
        run("build-user-data", ["dotnet", "build", "scripts/user-data-qa/UserDataQa.csproj", "-c", "Release", "--artifacts-path", str(work / "data-artifacts"), "--nologo"])
        run("build-ui-collector", ["dotnet", "build", "scripts/ui-coverage/UiCoverage.csproj", "-c", "Release", "--artifacts-path", str(work / "ui-artifacts"), "--nologo"])
        collector = work / "ui-artifacts/bin/UiCoverage/release/UiCoverage.dll"
        shutil.copy2(work / "appsettings.json", collector.parent / "appsettings.json")
        shutil.copytree(work / "app/Assets", collector.parent / "Assets", dirs_exist_ok=True)
        loader = Path("runtimes/win-x64/native/WebView2Loader.dll")
        (collector.parent / loader).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(work / "app" / loader, collector.parent / loader)
        # The collector must own both its fixture scratch and evidence paths.
        # Non-UI tools keep the narrower temp child directory configured above.
        ui_env = {**env, "TEMP": str(work), "TMP": str(work)}

        def ui_args(name):
            return ["dotnet", str(collector), str(work / "app"), "--" + name + "-qa", str(evidence / name / "result.json")]

        suites = runtime_commands(work, evidence, node, work / "temp/Infinity-user-data-fixture", ui_args)
        if len(suites) != len(contracts) or {name for name, _ in suites} != set(contracts):
            raise ValueError("Every contract suite requires exactly one runtime check")
        for label, args in suites:
            result = evidence / "extension-tests.json" if label == "extensions" else evidence / label / "result.json"
            if result.exists():
                raise ValueError("Unexpected preexisting result")
            run(label, args, environment=ui_env if str(collector) in args else None)
            checks[-1]["assertions"] = validate_result(result, contracts[label])
            if str(collector) in args:
                trace = json.loads(result.with_suffix(".ui-controls.json").read_text(encoding="utf-8-sig"))
                if trace["errors"]:
                    raise ValueError("WPF control collector failed: " + label)
                checks[-1]["controlsObserved"] = len(trace["controls"])
        summary["passed"] = True
        summary["assertions"] = sum(check.get("assertions", 0) for check in checks)
    finally:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print(json.dumps({"passed": summary["passed"], "work": str(work), "report": str(options.output)}))


if __name__ == "__main__":
    main()
