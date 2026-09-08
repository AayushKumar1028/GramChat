#!/usr/bin/env python3
"""Build a Debian .deb package from a staging directory.

Works on any host with Python 3 (no dpkg / binutils ar required), which lets
the InstaChat .deb be produced from the Windows development machine too.

Layout expected in the staging directory::

    <staging>/DEBIAN/control        (already substituted)
    <staging>/DEBIAN/postinst       (optional, mode 0755)
    <staging>/DEBIAN/postrm         (optional, mode 0755)
    <staging>/usr/...               (payload)

Usage::

    python3 tools/mkdeb.py <staging-dir> <output.deb>
"""

import hashlib
import io
import os
import stat
import struct
import sys
import tarfile
import time

# Executables that must keep their +x bit even when the package is built on a
# filesystem without executable permissions (e.g. cross-building from Windows).
FORCE_EXEC = {
    "usr/lib/instachat/InstaChat",
    "usr/bin/instachat",
}


def normalize_mode(path: str, mode: int) -> int:
    """Normalize payload modes: executables 0755, everything else 0644."""
    if path in FORCE_EXEC or mode & 0o111:
        return 0o755
    return 0o644


def md5sums(staging: str) -> str:
    lines = []
    for root, dirs, files in os.walk(os.path.join(staging, "usr")):
        for name in files:
            full = os.path.join(root, name)
            rel = os.path.relpath(full, staging).replace(os.sep, "/")
            with open(full, "rb") as fh:
                digest = hashlib.md5(fh.read()).hexdigest()
            lines.append(f"{digest}  {rel}")
    return "\n".join(sorted(lines)) + "\n"


def control_tar(staging: str, sums: str) -> bytes:
    buf = io.BytesIO()
    now = int(time.time())
    with tarfile.open(fileobj=buf, mode="w:gz", format=tarfile.GNU_FORMAT) as tar:
        control_path = os.path.join(staging, "DEBIAN", "control")
        if not os.path.isfile(control_path):
            sys.exit(f"error: {control_path} not found")
        with open(control_path, "rb") as fh:
            control = fh.read()
        for name, content in (("control", control), ("md5sums", sums.encode())):
            info = tarfile.TarInfo(f"./{name}")
            info.size = len(content)
            info.mode = 0o644
            info.mtime = now
            tar.addfile(info, io.BytesIO(content))
        for name in ("postinst", "postrm", "preinst", "prerm"):
            path = os.path.join(staging, "DEBIAN", name)
            if not os.path.isfile(path):
                continue
            info = tarfile.TarInfo(f"./{name}")
            info.size = os.path.getsize(path)
            info.mode = 0o755
            info.mtime = int(time.time())
            with open(path, "rb") as fh:
                tar.addfile(info, fh)
    return buf.getvalue()


def data_tar(staging: str) -> bytes:
    buf = io.BytesIO()
    now = int(time.time())
    with tarfile.open(fileobj=buf, mode="w:gz", format=tarfile.GNU_FORMAT) as tar:
        for root, dirs, files in os.walk(os.path.join(staging, "usr")):
            dirs.sort()
            files.sort()
            for name in files:
                full = os.path.join(root, name)
                rel = os.path.relpath(full, staging).replace(os.sep, "/")
                st = os.lstat(full)
                if stat.S_ISLNK(st.st_mode):
                    info = tarfile.TarInfo(f"./{rel}")
                    info.type = tarfile.SYMTYPE
                    info.linkname = os.readlink(full)
                    info.mode = 0o777
                else:
                    info = tarfile.TarInfo(f"./{rel}")
                    info.size = st.st_size
                    info.mode = normalize_mode(rel, stat.S_IMODE(st.st_mode))
                info.mtime = now
                info.uid = 0
                info.gid = 0
                if stat.S_ISLNK(st.st_mode):
                    tar.addfile(info)
                else:
                    with open(full, "rb") as fh:
                        tar.addfile(info, fh)
        # Explicit directories keep the tree tidy after upgrades.
        for root, dirs, files in os.walk(os.path.join(staging, "usr")):
            for name in dirs:
                full = os.path.join(root, name)
                rel = os.path.relpath(full, staging).replace(os.sep, "/")
                info = tarfile.TarInfo(f"./{rel}/")
                info.type = tarfile.DIRTYPE
                info.mode = 0o755
                info.mtime = now
                info.uid = 0
                info.gid = 0
                tar.addfile(info)
    return buf.getvalue()


def ar_field(value: str, width: int) -> bytes:
    """Space-padded fixed-width field, like GNU ar writes."""
    raw = value.encode()
    assert len(raw) <= width
    return raw + b" " * (width - len(raw))


def ar_member(name: str, payload: bytes, mode: int = 0o644) -> bytes:
    now = str(int(time.time()))
    header = (
        ar_field(name + "/", 16)
        + ar_field(now, 12)
        + ar_field("0", 6)
        + ar_field("0", 6)
        + ar_field(format(mode, "o"), 8)
        + ar_field(str(len(payload)), 10)
        + b"`\n"
    )
    pad = b"\n" if len(payload) % 2 else b""
    return header + payload + pad


def build_deb(staging: str, output: str) -> None:
    if not os.path.isfile(os.path.join(staging, "DEBIAN", "control")):
        sys.exit(f"error: {staging}/DEBIAN/control not found")

    sums = md5sums(staging)
    control = control_tar(staging, sums)
    data = data_tar(staging)

    deb = (
        b"!<arch>\n"
        + ar_member("debian-binary", b"2.0\n")
        + ar_member("control.tar.gz", control)
        + ar_member("data.tar.gz", data)
    )

    os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
    with open(output, "wb") as fh:
        fh.write(deb)
    print(f"wrote {output} ({len(deb)} bytes)")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: python3 tools/mkdeb.py <staging-dir> <output.deb>")
    build_deb(sys.argv[1], sys.argv[2])