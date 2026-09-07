"""Attaches the built zip to the GitHub release for a tag (creates the release if needed).

Uses the GitHub credential stored by Git Credential Manager (the same one used for
git push) - the token is read in-process and never printed.

Usage:  python tools/attach_release_asset.py <tag> <asset-file> [--apply-notes]
Example: python tools/attach_release_asset.py v1.0-beta2 dist/InstaChatAccess-1.0-beta2-win-x64.zip

--apply-notes also sets the release title (convention: "v1.0-beta2" -> "v.1.0-beta2")
and the release description.
"""

import json
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

OWNER_REPO = "AayushKumar1028/Insta-chat"

RELEASE_NOTES = """Chat-only Instagram for Windows - direct messages without Reels, Explore, Feed or Stories.

**Download `InstaChatAccess-{zip_base}-win-x64.zip` below, unzip it and run `InstaChatAccess.exe`.**

- Direct messages only - no Reels, Explore, Feed or Stories (navigation is hard-blocked)
- Self-contained single-file exe: no .NET, Node.js or other prerequisites required
- Privacy: your password is never stored; session data is kept in an encrypted local profile;
  `Privacy -> Log out & clear local data` wipes everything

Note: the build is not code-signed, so Windows SmartScreen may warn on first run -
choose **More info -> Run anyway**.

Requires: Windows 10 or newer with the Edge WebView2 runtime (pre-installed on up-to-date PCs).
"""


def get_token() -> str | None:
    """Read the GitHub credential from Git Credential Manager without printing it."""
    try:
        proc = subprocess.run(
            ["git", "credential", "fill"],
            input="protocol=https\nhost=github.com\n\n",
            capture_output=True,
            text=True,
        )
    except OSError as exc:
        print(f"could not run git: {exc}")
        return None
    creds = {}
    for line in proc.stdout.splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            creds[key] = value
    return creds.get("password")


TOKEN = get_token()
if not TOKEN:
    sys.exit("No stored GitHub credential found (git credential fill returned nothing).")


def github_request(url: str, method: str = "GET", data: bytes | None = None,
                   headers: dict | None = None, raw: bool = False):
    """Returns (status, parsed-json-or-raw-bytes). Raises only for non-404 HTTP errors."""
    req = urllib.request.Request(url, method=method, data=data)
    req.add_header("Authorization", f"Bearer {TOKEN}")
    req.add_header("Accept", "application/vnd.github+json")
    req.add_header("User-Agent", "instachat-release-tool")
    for key, value in (headers or {}).items():
        req.add_header(key, value)
    try:
        with urllib.request.urlopen(req) as resp:
            body = resp.read()
            return resp.status, (body if raw else (json.loads(body) if body else None))
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return 404, None
        raise


def release_title(tag: str) -> str:
    """Release title convention: git tag v1.0-beta2 -> title v.1.0-beta2."""
    return tag[0] + "." + tag[1:] if tag.startswith("v") else tag


def main(tag: str, asset_path: Path, apply_notes: bool) -> None:
    asset_name = asset_path.name
    asset_size = asset_path.stat().st_size

    # 1. Find or create the release for the tag.
    status, release = github_request(
        f"https://api.github.com/repos/{OWNER_REPO}/releases/tags/{tag}")
    if status == 200:
        print(f"release for {tag} already exists (id {release['id']})")
    else:
        payload = {
            "tag_name": tag,
            "name": release_title(tag),
            "body": RELEASE_NOTES.format(tag=tag, zip_base=tag.lstrip("v")),
            "draft": False,
            "prerelease": True,
        }
        status, release = github_request(
            f"https://api.github.com/repos/{OWNER_REPO}/releases",
            "POST",
            json.dumps(payload).encode(),
            {"Content-Type": "application/json"},
        )
        if status != 201:
            sys.exit(f"creating release failed: HTTP {status}")
        print(f"created release for {tag} (id {release['id']})")

    # 2. Upload the asset unless an identical one is already attached.
    existing = next((a for a in release.get("assets", []) if a["name"] == asset_name), None)
    if existing and existing["size"] == asset_size and existing["state"] == "uploaded":
        print(f"asset {asset_name} already attached and up to date")
    else:
        if existing:
            github_request(
                f"https://api.github.com/repos/{OWNER_REPO}/releases/assets/{existing['id']}",
                "DELETE")
            print(f"deleted outdated asset {asset_name}")
        data = asset_path.read_bytes()
        upload_url = (f"https://uploads.github.com/repos/{OWNER_REPO}/releases/"
                      f"{release['id']}/assets?name={asset_name}")
        status, uploaded = github_request(
            upload_url, "POST", data, {"Content-Type": "application/zip"})
        if status != 201:
            sys.exit(f"upload failed: HTTP {status}")
        print(f"uploaded {asset_name} ({asset_size} bytes)")
        print(f"download URL: {uploaded['browser_download_url']}")

    # 3. Optionally apply the title and description.
    if apply_notes:
        payload = {"name": release_title(tag),
                   "body": RELEASE_NOTES.format(tag=tag, zip_base=tag.lstrip("v"))}
        status, _ = github_request(
            f"https://api.github.com/repos/{OWNER_REPO}/releases/{release['id']}",
            "PATCH",
            json.dumps(payload).encode(),
            {"Content-Type": "application/json"},
        )
        if status == 200:
            print(f"release title set to '{release_title(tag)}' and description applied")
        else:
            sys.exit(f"updating release notes failed: HTTP {status}")

    print(f"release page: https://github.com/{OWNER_REPO}/releases/tag/{tag}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if a != "--apply-notes"]
    apply_notes_flag = "--apply-notes" in sys.argv
    if len(args) != 2:
        sys.exit(__doc__)
    main(args[0], Path(args[1]), apply_notes_flag)
