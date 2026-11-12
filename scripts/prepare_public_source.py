"""Export an allowlisted committed snapshot into a new, private-history-free Git repository.
Output must be a NEW directory beneath C:\\tmp. No push or remote is configured.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT_FILES = {".gitignore", "LICENSE", "README.md", "SECURITY.md", "CONTRIBUTING.md", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md", "appsettings.example.json", "global.json", "NuGet.Config", "Launch.cmd", "launch-provider.ps1"}
PREFIXES = ("LocalAI.Core/", "LocalAI.Desktop/", "LocalAI.CommandTests/", "LocalAI.ExtensionTests/", "LocalAI.FileTests/", "LocalAI.SourceTests/", "LocalAI.StartupTests/", "LocalAI.Tests/", "native/", "installer/", ".github/")
DOCS = {"docs/BUILDING.md", "docs/PUBLIC_BETA.md"}
SCRIPTS = {"scripts/verify_release.py", "scripts/verification_contract.json", "scripts/test_verify_release.py", "scripts/reverify_release.py", "scripts/verify_canonical.py", "scripts/build_msi.py", "scripts/test_build_msi.py", "scripts/inspect_msi.py", "scripts/test_install_local_release.ps1", "scripts/Install-LocalRelease.ps1", "scripts/test_deepseek_bridge.cjs", "scripts/prepare_public_source.py", "scripts/cleanup_ci.py", "scripts/test_cleanup_ci.py"}
SCRIPT_PREFIXES = ("scripts/user-data-qa/", "scripts/ui-coverage/")

def allowed(name):
    return name in ROOT_FILES or name in DOCS or name in SCRIPTS or name.startswith(PREFIXES + SCRIPT_PREFIXES)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="HEAD")
    parser.add_argument("--output", required=True, type=Path)
    options=parser.parse_args()
    repo=Path(__file__).resolve().parents[1]
    git=lambda *args: subprocess.check_output(["git", *args],cwd=repo)
    revision=git("rev-parse", options.ref+"^{commit}").decode().strip()
    raw=options.output.absolute()
    root=raw.resolve()
    if not root.is_relative_to(Path("C:/tmp")) or root.exists():
        raise ValueError("Use a new owned directory under C:/tmp")
    for parent in (raw,*raw.parents):
        if parent.exists() and getattr(parent.lstat(),"st_file_attributes",0)&1024:
            raise ValueError("Reparse-point output paths are excluded")
    names=git("ls-tree","-rz","--name-only",revision).decode().split("\0")
    selected=[n for n in names if n and allowed(n)]
    if not ROOT_FILES.issubset(set(selected)):
        raise ValueError("Missing required public source files")
    root.mkdir(parents=True)
    manifest=[]
    for name in selected:
        if Path(name).is_absolute() or ".." in Path(name).parts or Path(name).name.lower()=="appsettings.json":
            raise ValueError("Unsafe/private export member")
        mode=git("ls-tree",revision,"--",name).decode().split()[0]
        if mode!="100644" and mode!="100755":
            raise ValueError("Only ordinary files can be exported")
        data=git("show",revision+":"+name)
        target=root/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
        manifest.append({"path":name,"sha256":hashlib.sha256(data).hexdigest()})
    (root/"PUBLIC_SOURCE.json").write_text(json.dumps({"canonicalRevision":revision,"files":manifest},indent=2)+"\n",encoding="utf-8")
    run=lambda *args: subprocess.run(["git",*args],cwd=root,check=True,capture_output=True)
    run("init","--initial-branch=main")
    # Keep committed bytes identical to the audited export and provenance hashes.
    run("config","core.autocrlf","false")
    run("add","--all")
    for record in manifest:
        staged=subprocess.check_output(["git","show",":"+record["path"]],cwd=root)
        if hashlib.sha256(staged).hexdigest()!=record["sha256"]:
            raise ValueError("Git filters changed audited source bytes: "+record["path"])
    run("-c","user.name=Infinity contributors","-c","user.email=contributors@users.noreply.github.com","-c","commit.gpgsign=false","commit","-m","Prepare Infinity public beta source")
    public=subprocess.check_output(["git","rev-parse","HEAD"],cwd=root).decode().strip()
    print(json.dumps({"canonicalRevision":revision,"publicRevision":public,"files":len(manifest),"output":str(root)}))

if __name__=="__main__":main()
