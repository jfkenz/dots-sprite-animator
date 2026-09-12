"""Create an auditable UPM release candidate with its sample license notices.

Run with Python 3 from anywhere. Output stays under the Unity project's Packages.
A passing NUnit result file is required.
This prepares an archive; it does not publish anything.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="1.0.0-rc.1")
    parser.add_argument("--test-results", type=Path, required=True)
    parser.add_argument("--support-email")
    args = parser.parse_args()
    if not all(c.isalnum() or c in ".-" for c in args.version):
        parser.error("Version must be a filename-safe semantic version.")
    result = ET.parse(args.test_results).getroot()
    if result.tag != "test-run" or int(result.get("failed", "-1")) != 0 or int(result.get("passed", "0")) == 0:
        parser.error("A completed, passing NUnit test run is required.")
    root = Path(__file__).resolve().parent.parent
    output = root.parent / ".framework-release~"
    output.mkdir(exist_ok=True)
    target = output / f"com.invertlab.spriteanimator-{args.version}.tgz"
    if target.exists():
        parser.error(f"Refusing to overwrite {target}; use a new candidate version.")
    roots = {"Runtime", "Editor", "Shaders", "Tests", "Samples~"}
    root_files = {"package.json", "README.md", "CHANGELOG.md", "LICENSE.md", "Third-Party Notices.txt"}
    docs = {"Documentation.md", "QuickStart.md", "Architecture.md", "PublicAPI.md", "AnimationEvents.md",
            "SocketGameplayAPI.md", "SystemRunOrder.md", "ReleaseReadiness.md", "Samples.md",
            "Validation-Full-2026-09-12.xml", "Validation-Clean-2026-09-12.xml",
            "Validation-Player-2026-09-12.txt"}
    files = {}
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(root)
        parts = relative.parts
        stem = relative.as_posix().removesuffix(".meta")
        include = (parts[0] in roots or stem in root_files or stem in roots or
                   (len(parts) >= 3 and parts[:2] == ("Samples~", "Starter")) or
                   stem == "Samples~/Starter" or
                   (len(parts) == 2 and parts[0] == "Documentation~" and parts[1].removesuffix(".meta") in docs))
        if include:
            files[relative.as_posix()] = path.read_bytes()
    manifest = json.loads(files["package.json"])
    manifest["version"] = args.version
    if args.support_email:
        manifest["author"]["email"] = args.support_email
    files["package.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    inventory = "".join(f"{hashlib.sha256(data).hexdigest()}  {name}\n" for name, data in sorted(files.items()))
    files["RELEASE-FILES.sha256"] = inventory.encode()
    with tarfile.open(target, "w:gz") as archive:
        for name, data in sorted(files.items()):
            entry = tarfile.TarInfo("package/" + name)
            entry.size = len(data)
            entry.mode = 0o644
            archive.addfile(entry, io.BytesIO(data))
    target.with_suffix(target.suffix + ".sha256").write_text(hashlib.sha256(target.read_bytes()).hexdigest() + "  " + target.name + "\n")
    print(f"Created {target}: {len(files)} files. Sample contents and license files preserved.")
    print(f"Tests: {result.get('passed')} passed, {result.get('skipped')} skipped. Review skipped cases before sale.")


if __name__ == "__main__":
    main()
