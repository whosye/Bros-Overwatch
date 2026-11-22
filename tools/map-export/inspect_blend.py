"""Read-only scene/material inventory for preparing an imported map for Unity."""
import bpy
from collections import Counter
import json
from pathlib import Path
import sys

output = Path(sys.argv[sys.argv.index('--') + 1])
output.parent.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
objects = []
for obj in scene.objects:
    objects.append({
        'name': obj.name, 'type': obj.type, 'parent': obj.parent.name if obj.parent else None,
        'position': list(obj.matrix_world.translation), 'scale': list(obj.scale),
        'hidden': obj.hide_get() or obj.hide_viewport or obj.hide_render,
        'collections': [c.name for c in obj.users_collection],
        'mesh': obj.data.name if obj.type == 'MESH' else None,
        'properties': {k: str(v)[:300] for k, v in obj.items()},
    })
materials = []
for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    materials.append({'name': mat.name, 'nodes': [
        {'name': n.name, 'type': n.type,
         'group': n.node_tree.name if n.type == 'GROUP' and n.node_tree else None,
         'image': n.image.name if n.type == 'TEX_IMAGE' and n.image else None,
         'inputs': {s.name: str(s.default_value) for s in n.inputs if hasattr(s, 'default_value')},
         'links': [{'from': l.from_node.name + ':' + l.from_socket.name,
                    'to': l.to_node.name + ':' + l.to_socket.name}
                   for s in n.inputs for l in s.links]}
        for n in mat.node_tree.nodes]})
report = {
    'blender': bpy.app.version_string,
    'counts': dict(Counter(o.type for o in scene.objects)),
    'meshes': len(bpy.data.meshes), 'vertices': sum(len(m.vertices) for m in bpy.data.meshes),
    'polygons': sum(len(m.polygons) for m in bpy.data.meshes),
    'collections': [{'name': c.name, 'objects': len(c.objects), 'hidden': c.hide_viewport or c.hide_render}
                    for c in bpy.data.collections],
    'images': [{'name': i.name, 'size': list(i.size), 'packed': bool(i.packed_file),
                'path': i.filepath, 'colorSpace': i.colorspace_settings.name} for i in bpy.data.images],
    'objects': objects, 'materials': materials,
}
output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: report[k] for k in ('blender', 'counts', 'meshes', 'vertices', 'polygons')}))
print('Material/image inventory:', len(materials), len(report['images']), flush=True)
