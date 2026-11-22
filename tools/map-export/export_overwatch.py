"""Export an OWM Blender map, its original textures and URP material graphs.

Run with Blender in background mode, then -- <Unity asset folder>.
Does not save the .blend, resize images, decimate or quantize geometry.
"""
import bpy
import hashlib
import json
from pathlib import Path
import sys
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).parent))
from overwatch_shader import generate, ident

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
(folder / 'Textures').mkdir(parents=True, exist_ok=True)
(folder / 'Shaders').mkdir(exist_ok=True)
scene = bpy.context.scene
source_hash = hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()


def point(v):
    return {'x': -float(v[0]), 'y': float(v[2]), 'z': -float(v[1])}


def vector4(value):
    if isinstance(value, (int, float)):
        return [float(value)] * 4
    result = list(value)
    return result + [1.0] * (4 - len(result))


objects = [o for o in scene.objects if o.type == 'MESH' and not o.hide_get()
           and not o.hide_viewport and not o.hide_render
           and not (o.name == 'Cube' and [c.name for c in o.users_collection] == ['Collection'])]
if not objects:
    raise RuntimeError('No visible OWM map geometry found.')
fallback = bpy.data.materials.new('Sapa_Default')
fallback.use_nodes = True
fallback.node_tree.nodes.clear()
fallback_shader = fallback.node_tree.nodes.new('ShaderNodeGroup')
fallback_shader.name = 'OverwatchShader'
fallback_shader.node_tree = bpy.data.node_groups['OWM: Basic']
fallback_shader.inputs['Color'].default_value = (0.8, 0.8, 0.8, 1)
fallback_output = fallback.node_tree.nodes.new('ShaderNodeOutputMaterial')
fallback.node_tree.links.new(fallback_shader.outputs['Shader'], fallback_output.inputs['Surface'])
for obj in objects:
    if not obj.material_slots:
        obj.data.materials.append(fallback)
materials = sorted({s.material for o in objects for s in o.material_slots if s.material}, key=lambda m: m.name)
groups = {m.node_tree.nodes.get('OverwatchShader').node_tree for m in materials}
styles, slots = generate(groups, folder / 'Shaders')
images = {}
for index, image in enumerate(bpy.data.images):
    if image.type in ('RENDER_RESULT', 'COMPOSITING'):
        continue
    if image.size[0] == 0 or image.size[1] == 0:
        continue
    if not image.packed_file:
        if image.source == 'GENERATED':
            continue
        raise RuntimeError('Texture is not packed: ' + image.name)
    data = bytes(image.packed_file.data)
    if not data.startswith(b'\x89PNG\r\n\x1a\n'):
        raise RuntimeError('Expected original PNG image: ' + image.name)
    filename = f'{index:04d}_{ident(image.name.rsplit(".", 1)[0])}.png'
    (folder / 'Textures' / filename).write_bytes(data)
    images[image] = {
        'name': image.name, 'file': 'Textures/' + filename,
        'width': image.size[0], 'height': image.size[1],
        'linear': image.colorspace_settings.name in ('Non-Color', 'Linear', 'Linear Rec.709'),
        'sha256': hashlib.sha256(data).hexdigest(),
    }

material_info = []
for material in materials:
    shader = material.node_tree.nodes.get('OverwatchShader')
    if shader is None:
        raise RuntimeError('Material is not an OWM node graph: ' + material.name)
    inputs = []
    for socket in shader.inputs:
        record = {'name': ident(socket.name), 'value': vector4(socket.default_value), 'mode': 0, 'uv': 0, 'texture': ''}
        if socket.is_linked:
            link = socket.links[0]
            node = link.from_node
            if node.type != 'TEX_IMAGE' or node.image not in images:
                raise RuntimeError('Unsupported/missing material input: ' + material.name + '/' + socket.name)
            record['texture'] = images[node.image]['file']
            record['mode'] = 2 if link.from_socket.name == 'Alpha' else 1
            if node.inputs['Vector'].is_linked:
                uv_node = node.inputs['Vector'].links[0].from_node
                if uv_node.type != 'UVMAP':
                    raise RuntimeError('Unsupported texture coordinates: ' + material.name)
                record['uv'] = {'UVMap1': 0, 'UVMap2': 1, 'UVMap3': 2, '': 0}[uv_node.uv_map]
        inputs.append(record)
    group = shader.node_tree.name
    source_name = material.name
    material.name = 'OWM_' + source_name
    material_info.append({'name': material.name, 'sourceName': source_name,
                          'style': styles[group], 'group': group,
                          'transparent': group in ('OWM: Glass', 'OWM: Decal'), 'inputs': inputs})

descriptors = []
for index, obj in enumerate(objects):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = [min(v[i] for v in corners) for i in range(3)]
    high = [max(v[i] for v in corners) for i in range(3)]
    original_name = obj.name
    obj.name = f'Sapa_{index:05d}'
    # Distant scenic city/mountains, glass and projected decals are visual geometry.
    # Their vertices are exported unchanged; only gameplay collision is omitted.
    near_play_area = high[0] > -55 and low[0] < 125 and high[1] > -85 and low[1] < 85 and high[2] > -12 and low[2] < 55
    near_play_area = near_play_area and high[0] - low[0] < 400 and high[1] - low[1] < 400 and high[2] - low[2] < 180
    transparent_only = all(s.material and s.material.node_tree.nodes.get('OverwatchShader').node_tree.name
                           in ('OWM: Glass', 'OWM: Decal') for s in obj.material_slots)
    descriptors.append({'name': obj.name, 'sourceName': original_name,
                        'collidable': near_play_area and not transparent_only,
                        'materials': [s.material.name if s.material else '' for s in obj.material_slots],
                        'min': point((high[0], high[1], low[2])), 'max': point((low[0], low[1], high[2]))})

# Light transforms are metadata rather than punctual-light glTF extensions, so
# Unity receives exactly one object per source light and can set its shadow policy.
lights=[]
for light in scene.objects:
    if light.type != 'LIGHT' or light.name == 'Light' or light.hide_render or light.hide_get():
        continue
    direction = light.matrix_world.to_quaternion() @ Vector((0, 0, -1))
    lights.append({'name': light.name, 'type': light.data.type, 'position': point(light.matrix_world.translation),
                   'direction': point(direction), 'color': list(light.data.color), 'power': light.data.energy,
                   'spotAngle': getattr(light.data, 'spot_size', 1.0) * 180.0 / 3.141592653589793,
                   'radius': getattr(light.data, 'shadow_soft_size', 0.25)})

bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
# Preserve every primitive/material assignment in glTF. Unity replaces these
# named transport materials with the original OWM surface calculations above.
for material in materials:
    material.node_tree.nodes.clear()
    bsdf = material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
    material.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
options = dict(filepath=str(folder / 'Sapa.glb'), export_format='GLB', use_selection=True,
               export_apply=True, export_materials='EXPORT', export_animations=False,
               export_skins=False, export_morph=False, export_cameras=False, export_lights=False,
               export_normals=True, export_tangents=True, export_texcoords=True,
               export_vertex_color='NAME', export_vertex_color_name='ColorMap1',
               export_all_vertex_colors=True, export_active_vertex_color_when_no_material=True,
               export_extras=False, export_yup=True)
valid = set(bpy.ops.export_scene.gltf.get_rna_type().properties.keys())
unknown = set(options) - valid
if unknown:
    raise RuntimeError('Unsupported Blender export settings: ' + str(unknown))
result = bpy.ops.export_scene.gltf(**options)
if result != {'FINISHED'}:
    raise RuntimeError('glTF export did not complete.')
manifest = {'sourceSHA256': source_hash, 'objects': descriptors, 'materials': material_info,
            'textures': list(images.values()), 'lights': lights,
            'boundsCenter': {'x': -35, 'y': 20, 'z': 0}, 'boundsSize': {'x': 190, 'y': 70, 'z': 180},
            'geometryReduced': False, 'texturesResized': False}
(folder / 'Sapa.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'objects': len(objects), 'materials': len(materials), 'textures': len(images),
                  'lights': len(lights), 'glbBytes': (folder / 'Sapa.glb').stat().st_size}), flush=True)
