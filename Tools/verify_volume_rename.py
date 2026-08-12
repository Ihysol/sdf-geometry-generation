from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
COMPOSITION = ROOT / "Assets" / "Scripts" / "VolumeSystem" / "Composition"

EXPECTED = {
    "VolumeObjectRegistry.cs": "1db7fca8924706740a533d8d955ba297",
    "VolumeProcessor.cs": "390c39fbad2c5a4468a59eeef512bf0d",
}

for obsolete in ("VolumeSceneComposer.cs", "VolumeSceneComposer.cs.meta", "VolumeModel.cs", "VolumeModel.cs.meta"):
    assert not (COMPOSITION / obsolete).exists(), f"obsolete rename source remains: {obsolete}"

for script_name, expected_guid in EXPECTED.items():
    script = COMPOSITION / script_name
    meta = COMPOSITION / f"{script_name}.meta"
    assert script.exists(), f"missing renamed script: {script_name}"
    assert meta.exists(), f"missing meta: {meta.name}"
    match = re.search(r"(?m)^guid:\s*([0-9a-f]+)\s*$", meta.read_text(encoding="utf-8-sig"))
    assert match, f"no GUID in {meta.name}"
    assert match.group(1) == expected_guid, (
        f"{meta.name} must preserve Unity GUID {expected_guid}; got {match.group(1)}"
    )

markers = []
for path in (ROOT / "Assets").rglob("*"):
    if not path.is_file() or path.suffix.lower() not in {".cs", ".unity", ".prefab", ".asset", ".meta"}:
        continue
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    for number, line in enumerate(text.splitlines(), 1):
        if re.match(r"^(<<<<<<<|=======|>>>>>>>)", line):
            markers.append(f"{path.relative_to(ROOT)}:{number}:{line}")
assert not markers, "merge markers remain:\n" + "\n".join(markers)

unmerged = subprocess.run(
    ["git", "ls-files", "-u"], cwd=ROOT, text=True, capture_output=True, check=True
).stdout
assert not unmerged.strip(), "unmerged Git index entries remain:\n" + unmerged

# No active source outside explicitly disabled legacy blocks may reference removed runtime types.
active_stale_references = []
for path in (ROOT / "Assets" / "Scripts").rglob("*.cs"):
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    if path.name == "VolumeModelOld.cs" and "#if LEGACY" in text:
        continue
    for number, line in enumerate(text.splitlines(), 1):
        if re.search(r"\b(VolumeModel|VolumeSceneComposer)\b", line):
            active_stale_references.append(f"{path.relative_to(ROOT)}:{number}:{line.strip()}")
assert not active_stale_references, "active stale type references remain:\n" + "\n".join(active_stale_references)

# Every GUID is unique among tracked/working Assets meta files.
guid_owners = {}
for meta in (ROOT / "Assets").rglob("*.meta"):
    text = meta.read_text(encoding="utf-8-sig", errors="replace")
    match = re.search(r"(?m)^guid:\s*([0-9a-f]+)\s*$", text)
    if not match:
        continue
    guid_owners.setdefault(match.group(1), []).append(meta.relative_to(ROOT))
duplicates = {guid: owners for guid, owners in guid_owners.items() if len(owners) > 1}
assert not duplicates, "duplicate Unity GUIDs:\n" + "\n".join(
    f"{guid}: {', '.join(map(str, owners))}" for guid, owners in duplicates.items()
)

print("PASS: volume rename, Git conflict state, stale references, markers, and Unity GUIDs are valid")
