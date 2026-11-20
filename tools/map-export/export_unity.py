"""Export the compact SourceIO scene as portable PBR glTF plus gameplay markers.

Run: blender -b --disable-autoexec compact.blend --python export_unity.py -- output-folder
The source .blend is never saved or modified on disk.
"""
import bpy
import json
import math
from pathlib import Path
import sys
import time
import numpy as np
from mathutils import Vector

out = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
out.mkdir(parents=True, exist_ok=True)
started = time.time()

def unity_point(p):
    # Blender -> glTF (x,z,-y) -> Unity glTFast (-x,y,z).
    return {'x': -float(p[0]), 'y': float(p[2]), 'z': -float(p[1])}

def spawns(collection):
    group = bpy.data.collections.get(collection)
    if group is None:
        raise RuntimeError('Missing CS2 spawn markers: ' + collection)
    result = []
    for obj in sorted(group.all_objects, key=lambda o: o.name):
        result.append(unity_point(obj.matrix_world.translation))
    if not result:
        raise RuntimeError('No spawn positions in ' + collection)
    return result

team0 = spawns('info_player_terrorist')
team1 = spawns('info_player_counterterrorist')
source_materials = list(bpy.data.materials)
metadata = {}
converted = {}
unsupported = []

def find_image(nodes, names):
    for name in names:
        node = nodes.get(name)
        if node and node.type == 'TEX_IMAGE' and node.image:
            return node
    return None

def value(group, name, fallback):
    socket = group.inputs.get(name) if group else None
    return socket.default_value if socket and not socket.is_linked else fallback

for original in source_materials:
    nodes = original.node_tree.nodes if original.node_tree else None
    if nodes is None:
        continue
    shader = next((n for n in nodes if n.type in {'GROUP','BSDF_PRINCIPLED'} and
                   ('vfx' in n.name or n.type == 'BSDF_PRINCIPLED')), None)
    shader_name = shader.name.lower() if shader else ''
    omit = (original.name.startswith('tools') or 'effects' in shader_name or
            any(x in original.name.lower() for x in ('lightshaft', 'steam_001', 'dust_001')))
    color = find_image(nodes, ('g_tColor', 'g_tLayer1Color'))
    normal = find_image(nodes, ('g_tNormal', 'g_tLayer1NormalRoughness'))
    tint_input = shader.inputs.get('m_vColorTint') if shader else None
    if tint_input is None and shader:
        tint_input = shader.inputs.get('ModelTint')
    vertex_tint = bool(tint_input and tint_input.is_linked and
                       tint_input.links[0].from_node.type == 'VERTEX_COLOR')
    uv_name = 'TEXCOORD'
    scale, offset, center, rotation = (1,1), (0,0), (0.5,0.5), 0.0
    if color and color.inputs['Vector'].is_linked:
        transform = color.inputs['Vector'].links[0].from_node
        if transform.type == 'GROUP' and transform.node_tree and 'UVTransform' in transform.node_tree.name:
            scale = tuple(value(transform,'g_vTexCoordScale',(1,1,0)))[:2]
            offset = tuple(value(transform,'g_vTexCoordOffset',(0,0,0)))[:2]
            center = tuple(value(transform,'g_vTexCoordCenter',(0.5,0.5,0)))[:2]
            rotation = float(value(transform,'g_flTexCoordRotation',0))
            uv_socket = transform.inputs.get('UV')
            if uv_socket and uv_socket.is_linked:
                uv_name = getattr(uv_socket.links[0].from_node,'uv_map','TEXCOORD')
        elif transform.type == 'UVMAP':
            uv_name = transform.uv_map
        else:
            unsupported.append({'material': original.name, 'uv_node': transform.name})
    alpha_socket = shader.inputs.get('Alpha') if shader else None
    clip = bool(alpha_socket and alpha_socket.is_linked)
    foliage = 'foliage' in shader_name
    mat = bpy.data.materials.new('Export_' + original.name)
    mat.use_nodes = True
    graph = mat.node_tree
    graph.nodes.clear()
    output = graph.nodes.new('ShaderNodeOutputMaterial')
    pbr = graph.nodes.new('ShaderNodeBsdfPrincipled')
    pbr.inputs['Roughness'].default_value = 0.85
    pbr.inputs['Metallic'].default_value = 0.0
    global_tint = tuple(value(shader,'g_vColorTint',value(shader,'GlobalTint0',(1,1,1,1))))
    pbr.inputs['Base Color'].default_value = (*global_tint[:3],1)
    graph.links.new(pbr.outputs['BSDF'],output.inputs['Surface'])
    if color:
        texture = graph.nodes.new('ShaderNodeTexImage')
        texture.image = color.image
        graph.links.new(texture.outputs['Color'],pbr.inputs['Base Color'])
        if clip:
            threshold = graph.nodes.new('ShaderNodeMath')
            threshold.operation = 'GREATER_THAN'
            threshold.inputs[1].default_value = 0.5
            graph.links.new(texture.outputs['Alpha'],threshold.inputs[0])
            graph.links.new(threshold.outputs[0],pbr.inputs['Alpha'])
    if normal:
        texture = graph.nodes.new('ShaderNodeTexImage')
        texture.image = normal.image
        bump = graph.nodes.new('ShaderNodeNormalMap')
        graph.links.new(texture.outputs['Color'],bump.inputs['Color'])
        graph.links.new(bump.outputs['Normal'],pbr.inputs['Normal'])
    mat.use_backface_culling = not foliage
    converted[original.name] = mat
    metadata[original.name] = {'name':original.name,'omit':omit,'collidable':not omit and not any(
        word in original.name.lower() for word in
        ('weeds','plant','branch','frond','vine','leaves','grass','rope','wire_','spray','overlay_sign')),
        'vertex_tint':vertex_tint, 'tint':global_tint[:3], 'uv_name':uv_name,
        'scale':scale,'offset':offset,'center':center,'rotation':rotation}

deps = bpy.context.evaluated_depsgraph_get()
instances = [(i.object.original, i.matrix_world.copy(),
              i.parent.original.color[:] if i.is_instance and i.parent else i.object.original.color[:])
             for i in deps.object_instances if i.object.type == 'MESH' and i.show_self]
export_scene = bpy.data.scenes.new('Dust2 Export')
bpy.context.window.scene = export_scene
cache = {}
exported = 0
omitted = 0
points = []
renderers = []
for obj, matrix, object_tint in instances:
    mats = [m for m in obj.data.materials if m]
    if not mats or all(metadata.get(m.name,{}).get('omit',True) for m in mats):
        omitted += 1
        continue
    if len(mats) != 1:
        raise RuntimeError('Expected one material per SourceIO mesh: ' + obj.name)
    info = metadata[mats[0].name]
    cache_key = (obj.data.as_pointer(), tuple(object_tint) if not info['vertex_tint'] else None)
    mesh = cache.get(cache_key)
    if mesh is None:
        mesh = obj.data.copy()
        mesh.name = 'Dust2_' + obj.data.name
        uv = mesh.uv_layers.get(info['uv_name']) or mesh.uv_layers.active
        if uv is not None:
            uv_name = uv.name
            for other in list(mesh.uv_layers):
                if other.name != uv_name:
                    mesh.uv_layers.remove(other)
            uv = mesh.uv_layers[0]
            coords = np.empty(len(uv.data)*2,dtype=np.float32)
            uv.data.foreach_get('uv',coords)
            coords = coords.reshape(-1,2)
            coords -= np.array(info['center'])
            coords *= np.array(info['scale'])
            angle = info['rotation']
            if abs(angle)>0.000001:
                c,s = math.cos(angle),math.sin(angle)
                coords = coords @ np.array(((c,s),(-s,c)))
            coords += np.array(info['center']) + np.array(info['offset'])
            uv.data.foreach_set('uv',coords.ravel())
        tint = mesh.color_attributes.get('TINT')
        rgba = np.ones((len(mesh.loops),4),dtype=np.float32)
        if info['vertex_tint'] and tint:
            if tint.domain != 'CORNER':
                raise RuntimeError('Unexpected tint domain: ' + obj.name)
            tint.data.foreach_get('color',rgba.ravel())
        else:
            rgba[:,:3] = object_tint[:3]
        rgba[:,:3] *= np.array(info['tint'])
        rgba[:,3] = 1
        for attr in list(mesh.color_attributes):
            mesh.color_attributes.remove(attr)
        attr = mesh.color_attributes.new(name='ExportTint',type='BYTE_COLOR',domain='CORNER')
        attr.data.foreach_set('color',rgba.ravel())
        mesh.materials.clear()
        mesh.materials.append(converted[mats[0].name])
        cache[cache_key] = mesh
    name = f'D2_{exported:04d}_{obj.name}'
    clone = bpy.data.objects.new(name,mesh)
    export_scene.collection.objects.link(clone)
    clone.matrix_world = matrix
    renderers.append({'name':clone.name,'collidable':info['collidable']})
    points += [unity_point(matrix @ Vector(c)) for c in obj.bound_box]
    exported += 1

print('DUST2_EXPORT_OBJECTS',exported,'omitted',omitted,'elapsed',time.time()-started,flush=True)
model = out / 'Dust2.glb'
bpy.ops.export_scene.gltf(filepath=str(model),export_format='GLB',use_active_scene=True,
    export_animations=False,export_cameras=False,export_lights=False,export_skins=False,
    export_morph=False,export_yup=True,export_normals=True,export_tangents=False,
    export_vertex_color='NAME',export_vertex_color_name='ExportTint',export_all_vertex_colors=False,
    export_image_format='AUTO',export_image_quality=80,export_extras=False)
sys.path.insert(0,str(Path(__file__).parent))
from compact_glb import compact_attributes
compact_attributes(model)
assert model.stat().st_size < 100_000_000, 'Map exceeds the source asset budget'
low = {axis:min(p[axis] for p in points) for axis in ('x','y','z')}
high = {axis:max(p[axis] for p in points) for axis in ('x','y','z')}
manifest={'team0':team0,'team1':team1,'renderers':renderers,
          'boundsCenter':{k:(low[k]+high[k])/2 for k in low},
          'boundsSize':{k:high[k]-low[k] for k in low},
          'exportBytes':model.stat().st_size,'sourceObjects':exported,
          'omittedUtilityObjects':omitted,'uvWarnings':unsupported}
(out/'Dust2.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('DUST2_EXPORT_DONE',model.stat().st_size,'bytes',len(team0),len(team1),'spawns',flush=True)
