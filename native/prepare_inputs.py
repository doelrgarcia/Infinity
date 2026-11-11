"""Prepare pinned public native build inputs; downloads require --download.

Use --runtime-directory to reuse an installed publisher runtime. Its GenAI DLL
must match the pinned hash; existing files are never overwritten on mismatch.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import urllib.request
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(url, destination, maximum, expected):
    with urllib.request.urlopen(url, timeout=60) as response, destination.open("wb") as output:
        if not response.geturl().startswith("https://"):
            raise ValueError("Native input redirect downgraded HTTPS")
        size = 0
        while block := response.read(1024 * 1024):
            size += len(block)
            if size > maximum:
                raise ValueError("Native input exceeds its bounded download size")
            output.write(block)
    if digest(destination) != expected:
        raise ValueError("Pinned public input checksum failed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--runtime-directory", type=Path)
    parser.add_argument("--download", action="store_true")
    args = parser.parse_args()
    root = args.directory.resolve()
    root.mkdir(parents=True, exist_ok=True)
    here = Path(__file__).resolve().parent
    pins = json.loads((here / "inputs.json").read_text(encoding="utf-8-sig"))
    for pin in pins:
        target = root / pin["name"]
        if target.exists():
            if digest(target) != pin["sha256"]:
                raise ValueError("Existing input differs; choose a clean cache: " + str(target))
            continue
        with tempfile.TemporaryDirectory(prefix="Infinity-input-", dir=root) as scratch:
            temporary = Path(scratch) / "input"
            if pin["name"] == "onnxruntime-genai.dll":
                installed = args.runtime_directory / pin["name"] if args.runtime_directory else None
                if installed and installed.is_file():
                    shutil.copy2(installed, temporary)
                else:
                    if not args.download:
                        raise ValueError("Provide the installed pinned runtime or enable public downloads")
                    catalog = json.loads((here.parent / "LocalAI.Core/dependency-catalog.json").read_text(encoding="utf-8-sig"))
                    artifact = next(p for p in catalog["Packages"] if p["Id"] == "ryzen-runtime")["Artifacts"][0]
                    archive_path = Path(scratch) / "runtime.zip"
                    download(artifact["Url"], archive_path, artifact["Bytes"], artifact["Sha256"].lower())
                    with zipfile.ZipFile(archive_path) as archive:
                        members = [item for item in archive.infolist() if item.filename.replace("\\", "/").split("/")[-1] == pin["name"]]
                        if len(members) != 1 or members[0].file_size > 128 * 1024 * 1024:
                            raise ValueError("Ambiguous runtime DLL archive entry")
                        with archive.open(members[0]) as source, temporary.open("wb") as output:
                            shutil.copyfileobj(source, output)
            else:
                if not args.download:
                    raise ValueError("Missing pinned input: " + pin["name"])
                download(pin["url"], temporary, 20 * 1024 * 1024, pin["sha256"])
            if digest(temporary) != pin["sha256"]:
                raise ValueError("Pinned input checksum failed: " + pin["name"])
            temporary.replace(target)
    print("Six pinned public native inputs verified. No installer or downloaded code was executed.")


if __name__ == "__main__":
    main()
