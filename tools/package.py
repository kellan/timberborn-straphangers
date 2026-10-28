#!/usr/bin/env python3
"""Build a local mod package, deriving diagnostic reference data from your game."""
import argparse, hashlib, json, shutil, zipfile
from pathlib import Path


def station_reference(managed, blueprints):
    templates = {}
    with zipfile.ZipFile(blueprints) as source:
        for template, path in {
            'TubewayStation.IronTeeth': 'Buildings/Paths/TubewayStation/TubewayStation.IronTeeth.blueprint.json',
            'ZiplineStation.Folktails': 'Buildings/Paths/ZiplineStation/ZiplineStation.Folktails.blueprint.json',
        }.items():
            data = json.loads(source.read(path))
            block = data['BlockObjectSpec']
            groups = data['BlockObjectNavMeshSettingsSpec']['EdgeGroups']
            # The auditor needs only the authored pedestrian doorway, not models,
            # textures, building text or the rest of the game's blueprint data.
            templates[template] = {
                'source': path,
                'block': {'Entrance': block['Entrance'], 'Flippable': block['Flippable']},
                'navigation': {'EdgeGroups': [g for g in groups if not g['Group']['UseGroup']]},
            }
    return {
        'schema': 1,
        'movement_assembly_sha256': hashlib.sha256((managed / 'Timberborn.CharacterMovementSystem.dll').read_bytes()).hexdigest(),
        'templates': templates,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--managed', type=Path, required=True)
    parser.add_argument('--blueprints', type=Path, help='Override the installed Modding/Blueprints.zip location')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    dll = root / 'Source/bin/Release/netstandard2.1/BoardingCounter.dll'
    if not dll.is_file():
        parser.error('Build the mod first using build.sh or dotnet build.')
    blueprints = args.blueprints or args.managed.parent / 'StreamingAssets/Modding/Blueprints.zip'
    if not blueprints.is_file():
        parser.error('Cannot find Blueprints.zip; supply --blueprints explicitly.')
    reference = station_reference(args.managed, blueprints)
    folder = root / 'artifacts/Straphangers'
    (folder / 'Replay').mkdir(parents=True, exist_ok=True)
    for source, target in [(dll, folder / dll.name), (root / 'Mod/manifest.json', folder / 'manifest.json'), (root / 'README.md', folder / 'README.md')]:
        shutil.copy2(source, target)
    shutil.copy2(root / 'Replay/audit_recording.py', folder / 'Replay/audit_recording.py')
    shutil.copy2(root / 'Replay/HOW-TO-RECORD.txt', folder / 'Replay/HOW-TO-RECORD.txt')
    (folder / 'Replay/station-reference.json').write_text(json.dumps(reference, separators=(',', ':')) + '\n')
    version = json.loads((folder / 'manifest.json').read_text())['Version']
    archive = root / 'artifacts' / ('Straphangers-' + version + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        for file in sorted(folder.rglob('*')):
            if file.is_file():
                output.write(file, str(file.relative_to(folder.parent)))
    print('Local package:', archive)
    print('Game DLLs are referenced for compilation, never copied into the package.')


if __name__ == '__main__':
    main()
