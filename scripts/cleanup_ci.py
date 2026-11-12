"""Remove only a generated Infinity CI directory; never follow reparse targets."""
import os
from pathlib import Path
import re
import stat
from collections import Counter


def cleanup(directory, temporary_root=Path("C:/tmp")):
    raw = Path(directory).absolute()
    root = raw.resolve()
    base = Path(temporary_root).resolve()
    if root.parent != base or not re.fullmatch(r"Infinity-ci-[0-9a-f]{32}", root.name):
        raise ValueError("Unexpected CI cleanup directory")
    if raw.exists() and getattr(raw.lstat(), "st_file_attributes", 0) & 1024:
        raise ValueError("Linked CI cleanup root")
    if not root.exists():
        return

    links = Counter()

    def inventory(path):
        for entry in os.scandir(path):
            metadata = Path(entry.path).lstat()
            if getattr(metadata, "st_file_attributes", 0) & 1024:
                continue
            if stat.S_ISDIR(metadata.st_mode):
                inventory(Path(entry.path))
            else:
                links[(metadata.st_dev, metadata.st_ino)] += 1

    inventory(root)

    def remove(path):
        metadata = path.lstat()
        if getattr(metadata, "st_file_attributes", 0) & 1024:
            if stat.S_ISLNK(metadata.st_mode):
                path.unlink()
            elif getattr(metadata, "st_file_attributes", 0) & 16:
                os.rmdir(path)
            else:
                path.unlink()
        elif stat.S_ISDIR(metadata.st_mode):
            for entry in list(os.scandir(path)):
                remove(Path(entry.path))
            path.rmdir()
        else:
            if getattr(metadata, "st_file_attributes", 0) & 1:
                if links[(metadata.st_dev, metadata.st_ino)] < metadata.st_nlink:
                    raise PermissionError("Read-only file has links outside the owned CI directory")
                os.chmod(path, stat.S_IREAD | stat.S_IWRITE)
            path.unlink()

    remove(root)
    if root.exists():
        raise OSError("CI cleanup did not finish")


if __name__ == "__main__":
    value = os.environ.get("INFINITY_CI_WORK")
    if value:
        cleanup(value)
