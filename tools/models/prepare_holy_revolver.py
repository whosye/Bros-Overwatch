"""Extract the closed upper revolver without changing its UV layout."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / '_MIMO_GIT_K_PRESUNU/Holy Revolver'
output = ROOT / 'OverwatchBros/Assets/Models/Weapons/HolyRevolver'
output.mkdir(parents=True, exist_ok=True)
lines = ['# Holy Revolver: closed upper gun only, original UVs and normals preserved.']
offset = 0
for source_part, part in ((2, 0), (1, 1)):
    vertices, uv, normals, faces = [], [], [], []
    for line in (source / f'model_{source_part}.obj').read_text().splitlines():
        p = line.split()
        if not p:
            continue
        if p[0] == 'v':
            vertices.append(tuple(map(float, p[1:4])))
        elif p[0] == 'vt':
            uv.append(tuple(map(float, p[1:3])))
        elif p[0] == 'vn':
            normals.append(tuple(map(float, p[1:4])))
        elif p[0] == 'f':
            indices = [tuple(int(x) - 1 for x in token.split('/')) for token in p[1:]]
            assert all(a == b == c for a, b, c in indices), 'Source layout changed'
            f = [a for a, _, _ in indices]
            if len(set(f)) < 3:
                continue  # Degenerate strip connectors, including bridges between the two guns.
            if max(vertices[i][1] for i in f) < 300:
                continue
            assert min(vertices[i][1] for i in f) >= 300, 'A real face crosses the two-gun split'
            if min(vertices[i][1] for i in f) > 1100:
                continue  # Loose display cartridge floating above the closed gun.
            faces.append(f)
    used = sorted({i for face in faces for i in face})
    cylinder = set()
    if source_part == 2:
        parent = list(range(len(vertices)))
        def root(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i
        def join(a, b):
            parent[root(a)] = root(b)
        welded = {}
        for i in used:
            key = tuple(round(c, 3) for c in vertices[i])
            if key in welded:
                join(i, welded[key])
            welded[key] = i
        for a, b, c in faces:
            join(a, b)
            join(a, c)
        groups = {}
        for i in used:
            groups.setdefault(root(i), []).append(i)
        for group in groups.values():
            lo = [min(vertices[i][a] for i in group) for a in range(3)]
            hi = [max(vertices[i][a] for i in group) for a in range(3)]
            if 300 < lo[0] < 320 and 490 < hi[0] < 510 and hi[2] - lo[2] > 160:
                cylinder.update(group)
        assert len(cylinder) > 1000, 'Cannot identify the closed cylinder'
    remap = {old: offset + new + 1 for new, old in enumerate(used)}
    lines.append(f'o RevolverPart{part}')
    for i in used:
        x, y, z = vertices[i]
        x, y, z = 391.04 - x, y - 837.54, -z - 6.84
        # +Z muzzle, +Y up; upper grip aligned to HeldWeapons.GripInWeapon.
        lines.append(f'v {-z * .00025:.9f} {(y + 160) * .00025 - .07:.9f} {(x + 350) * .00025 + .05:.9f}')
    for i in used:
        lines.append('vt ' + ' '.join(map(str, uv[i])))
    for i in used:
        x, y, z = normals[i]
        x, z = -x, -z
        lines.append(f'vn {-z} {y} {x}')
    last_material = None
    for face in faces:
        material = 'Cylinder' if face[0] in cylinder else f'Part{part}'
        if material != last_material:
            lines.append('usemtl ' + material)
            last_material = material
        lines.append('f ' + ' '.join(f'{remap[i]}/{remap[i]}/{remap[i]}' for i in face))
    offset += len(used)
    print(f'Part {part}: {len(used)} vertices, {len(faces)} triangles')
(output / 'HolyRevolver.obj').write_text('\n'.join(lines) + '\n')
print(output)
