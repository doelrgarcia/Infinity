"""Build a per-user Infinity MSI from a passing immutable application package.

Private host settings never enter the payload. Build and extension-cache output
must stay in the caller's owned TEMP root; the final MSI is an explicit deliverable.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET

from verify_release import unique_object

NS = "http://wixtoolset.org/schemas/v4/wxs"
ET.register_namespace("", NS)
NAMESPACE = uuid.UUID("f295153d-46ad-4b33-b743-2241a553742d")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def no_links(path):
    for part in reversed((path, *path.parents)):
        try:
            attributes = part.lstat()
        except FileNotFoundError:
            continue
        if getattr(attributes, "st_file_attributes", 0) & 1024:
            raise ValueError("Linked package paths are excluded")


def package_path(base, member):
    relative = Path(member)
    if relative.is_absolute() or relative.drive or ".." in relative.parts:
        raise ValueError("Unsafe package member")
    no_links(base / relative)
    path = (base / relative).resolve()
    if not path.is_relative_to(base):
        raise ValueError("Unsafe package member")
    if path.name.lower() == "appsettings.json":
        raise ValueError("Private application configuration is excluded")
    return path


def validate_report(report):
    revision = report.get("sourceRevision", "")
    if report.get("passed") is not True or len(revision) != 40 or any(c not in "0123456789abcdef" for c in revision):
        raise ValueError("A passing immutable application verification is required")
    work = Path(report["work"]).resolve()
    contract = work / "scripts/verification_contract.json"
    records = [r for r in report["sourceFiles"] if r["path"] == "scripts/verification_contract.json"]
    if len(records) != 1 or digest(contract) != records[0]["sha256"]:
        raise ValueError("Application verification contract differs")
    expected = json.loads(contract.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    if report["assertions"] != sum(map(len, expected.values())):
        raise ValueError("Application verification is incomplete")
    runtime = [c for c in report["checks"] if c["name"] in expected]
    if len(runtime) != len(expected) or {c["name"] for c in runtime} != set(expected) or any(c["exitCode"] != 0 or c["assertions"] != len(expected[c["name"]]) for c in runtime):
        raise ValueError("Every exact application runtime suite must have passed")
    app = work / "app"
    seen = set()
    for row in report["packageFiles"]:
        key = row["path"].replace("\\", "/").lower()
        if key in seen:
            raise ValueError("Duplicate package member")
        seen.add(key)
        if digest(package_path(app, row["path"])) != row["sha256"]:
            raise ValueError("Verified application payload changed")
    if not {"localai.desktop.exe", "localai.core.dll", "native/infinity.ryzenhost.exe"}.issubset(seen):
        raise ValueError("Incomplete application payload")
    return work, app


def author_files(payload, output):
    wix = ET.Element(f"{{{NS}}}Wix")
    fragment = ET.SubElement(wix, "Fragment")
    root = ET.SubElement(fragment, "DirectoryRef", Id="INSTALLFOLDER")
    group = ET.SubElement(fragment, "ComponentGroup", Id="ApplicationFiles")
    directories = {".": (root, "INSTALLFOLDER")}

    def directory(relative):
        key = str(relative)
        if key not in directories:
            parent, _ = directory(relative.parent)
            identifier = "d" + hashlib.sha256(key.encode()).hexdigest()[:28]
            directories[key] = (ET.SubElement(parent, "Directory", Id=identifier, Name=relative.name), identifier)
        return directories[key]

    cleaned = set()
    for path in sorted(payload.rglob("*")):
        if not path.is_file() or path.name == "appsettings.json":
            continue
        relative = path.relative_to(payload)
        parent, folder = directory(relative.parent)
        identifier = "c" + hashlib.sha256(relative.as_posix().encode()).hexdigest()[:28]
        component = ET.SubElement(parent, "Component", Id=identifier, Guid="{" + str(uuid.uuid5(NAMESPACE, relative.as_posix().lower())) + "}")
        ET.SubElement(component, "File", Id="f" + identifier[1:], Source=str(path))
        ET.SubElement(component, "RegistryValue", Root="HKCU", Key=r"Software\Infinity\Installer\Files", Name=identifier, Value="1", Type="integer", KeyPath="yes")
        if folder not in cleaned and folder != "INSTALLFOLDER":
            ET.SubElement(component, "RemoveFolder", Id="r" + identifier[1:], On="uninstall")
            cleaned.add(folder)
        ET.SubElement(group, "ComponentRef", Id=identifier)
    ET.indent(wix)
    ET.ElementTree(wix).write(output, encoding="utf-8", xml_declaration=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--ui-extension", required=True, type=Path)
    parser.add_argument("--version", default="1.26.1109")
    args = parser.parse_args()
    root = args.work.resolve()
    root.relative_to(Path(tempfile.gettempdir()).resolve())
    no_links(root)
    if root.exists():
        raise ValueError("Use a new owned build folder")
    version = args.version.split(".")
    if len(version) != 3 or not all(v.isdigit() for v in version) or any(int(v) > limit for v, limit in zip(version, (255, 255, 65535))):
        raise ValueError("Use a valid three-part MSI version")
    report = json.loads(args.report.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    work, app = validate_report(report)
    repo = Path(__file__).resolve().parents[1]
    licensing=repo/"installer/third-party"
    pin=json.loads((licensing/"wix-ui-source-pin.json").read_text(encoding="utf-8"))
    if digest(args.ui_extension)!=pin["uiExtensionSha256"] or digest(licensing/"wix-ui-source-v7.0.0.zip")!=pin["uiSourceZipSha256"]:
        raise ValueError("WiX UI extension or supplied corresponding source differs from its pin")
    root.mkdir(parents=True)
    env = {**os.environ, "TEMP": str(root), "TMP": str(root), "PYTHONDONTWRITEBYTECODE": "1"}
    payload = root / "payload"
    payload.mkdir()
    for row in report["packageFiles"]:
        target = payload / row["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(package_path(app, row["path"]), target)
    icon = work / "LocalAI.Desktop/Assets/infinity.ico"
    icon_record = next(r for r in report["sourceFiles"] if r["path"] == "LocalAI.Desktop/Assets/infinity.ico")
    if digest(icon) != icon_record["sha256"]:
        raise ValueError("Application icon differs from the verified source")
    (payload / "Assets").mkdir(exist_ok=True)
    shutil.copy2(icon, payload / "Assets/infinity.ico")
    # Build a read-only GUI verifier. It does not run commands or inference.
    verifier = root / "verifier"
    with (root / "verifier-build.log").open("wb") as log:
        subprocess.run(["dotnet", "publish", str(repo / "installer/Verifier/Infinity.Verify.csproj"), "-c", "Release", "--artifacts-path", str(root / "build"), "-o", str(verifier), "-p:UseSharedCompilation=false", "-nodeReuse:false"], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    for path in verifier.rglob("*"):
        if not path.is_file() or path.suffix == ".pdb":
            continue
        target = payload / path.relative_to(verifier)
        if target.exists():
            if digest(target) != digest(path):
                raise ValueError("Verifier runtime differs from the verified application runtime")
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    settings = repo / "installer/default-settings.json"
    defaults = json.loads(settings.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    if set(defaults)-{"LoadTimeoutSeconds","GenerationTimeoutSeconds","Models"} or any(set(p)-{"Id","Label","Engine","Endpoint","ContextLimit"} for p in defaults["Models"]):
        raise ValueError("Distribution settings must contain only public profile defaults")
    shutil.copy2(settings, payload / "appsettings.json")
    notice=payload/"Assets/Licenses/WiXUI-NOTICE.txt"
    notice.parent.mkdir(parents=True,exist_ok=True)
    notice.write_text("WiX UI library: copyright .NET Foundation and contributors. Used unmodified under the Microsoft Reciprocal License.\nSource and license: https://github.com/wixtoolset/wix/tree/v7.0.0/src/ext/UI\nLicense text: https://github.com/wixtoolset/wix/blob/v7.0.0/LICENSE.TXT\n",encoding="utf-8")
    for name in ("WiXUI-LICENSE.txt","wix-ui-source-v7.0.0.zip"):
        shutil.copy2(licensing/name,notice.parent/name)
    rows = [{"path": p.relative_to(payload).as_posix(), "sha256": digest(p)} for p in sorted(payload.rglob("*")) if p.is_file() and p.name != "appsettings.json"]
    manifest = {"SourceRevision": report["sourceRevision"], "Files": rows, "Scope": "Public application and read-only verifier only; private settings and models excluded."}
    (payload / "installation-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    author_files(payload, root / "Files.wxs")
    output = args.output.resolve()
    no_links(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        raise ValueError("Installer destination already exists")
    product = "{" + str(uuid.uuid5(NAMESPACE, args.version + "|" + report["sourceRevision"])) + "}"
    with (root / "msi-build.log").open("wb") as log:
        subprocess.run(["wix", "build", "-arch", "x64", "-ext", str(args.ui_extension.resolve()), "-d", "Payload=" + str(payload), "-d", "Version=" + args.version, "-d", "ProductCode=" + product, "-d", "SourceRevision=" + report["sourceRevision"], "-intermediatefolder", str(root / "wix"), "-pdbtype", "none", "-o", str(output), str(repo / "installer/Infinity.wxs"), str(repo / "installer/InstallerUI.wxs"), str(root / "Files.wxs")], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    summary = {"built": True, "msi": str(output), "sha256": digest(output), "bytes": output.stat().st_size, "version": args.version, "productCode": product, "applicationSourceRevision": report["sourceRevision"], "applicationFiles": len(report["packageFiles"]), "verifierManifestFiles": len(rows), "privateHostConfigurationIncluded": False, "signing": "Unsigned beta candidate", "payload": str(payload), "definitionFiles": [{"path": str(repo / "installer" / name), "sha256": digest(repo / "installer" / name)} for name in ("Infinity.wxs", "InstallerUI.wxs", "default-settings.json", "Verifier/Program.cs", "Verifier/Infinity.Verify.csproj")]}
    output.with_suffix(".build.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    output.with_suffix(".sha256").write_text(summary["sha256"] + "  " + output.name + "\n", encoding="ascii")
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
