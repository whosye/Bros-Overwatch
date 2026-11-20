"""Pack glTF colors to RGBA8 and normals to normalized signed 16-bit vectors.

Uses KHR_mesh_quantization, supported natively by the project's glTFast importer.
No Draco/meshopt decoder is required. Positions, topology, UVs and images are unchanged.
"""
from array import array
import json
from pathlib import Path
import struct
import sys

def compact_attributes(path, destination=None):
    path = Path(path)
    data = path.read_bytes()
    magic,version,total = struct.unpack_from('<III',data)
    assert magic == 0x46546C67 and version == 2 and total == len(data)
    size,kind = struct.unpack_from('<II',data,12)
    assert kind == 0x4E4F534A
    doc = json.loads(data[20:20+size])
    bin_offset = 20+size
    bin_size,kind = struct.unpack_from('<II',data,bin_offset)
    assert kind == 0x004E4942
    binary = data[bin_offset+8:bin_offset+8+bin_size]
    colors = {p['attributes']['COLOR_0'] for m in doc['meshes'] for p in m['primitives']
              if 'COLOR_0' in p['attributes']}
    views = {}
    normal_views = set()
    for index in colors:
        accessor = doc['accessors'][index]
        if accessor['componentType'] == 5121:
            continue
        assert accessor['componentType'] == 5123 and accessor['normalized']
        assert accessor['type'] == 'VEC4' and accessor.get('byteOffset',0) == 0
        view = doc['bufferViews'][accessor['bufferView']]
        assert view.get('byteStride',8) == 8 and view['byteLength'] == accessor['count']*8
        values = array('H')
        offset = view.get('byteOffset',0)
        values.frombytes(binary[offset:offset+view['byteLength']])
        if sys.byteorder != 'little':
            values.byteswap()
        packed = bytes((v+128)//257 for v in values)
        assert all(abs(a-b*257)<=128 for a,b in zip(values,packed))
        views[accessor['bufferView']] = packed
        accessor['componentType'] = 5121
    normals = {p['attributes']['NORMAL'] for m in doc['meshes'] for p in m['primitives']
               if 'NORMAL' in p['attributes']}
    for index in normals:
        accessor = doc['accessors'][index]
        if accessor['componentType'] == 5122:
            continue
        assert accessor['componentType'] == 5126 and accessor['type'] == 'VEC3'
        assert accessor.get('byteOffset',0) == 0
        view_index = accessor['bufferView']
        view = doc['bufferViews'][view_index]
        assert view.get('byteStride',12) == 12 and view['byteLength'] == accessor['count']*12
        values = array('f')
        offset = view.get('byteOffset',0)
        values.frombytes(binary[offset:offset+view['byteLength']])
        if sys.byteorder != 'little':
            values.byteswap()
        packed = array('h')
        for i in range(0,len(values),3):
            xyz = [int(round(max(-1,min(1,v))*32767)) for v in values[i:i+3]]
            assert all(abs(a-b/32767)<=0.000016 for a,b in zip(values[i:i+3],xyz))
            packed.extend((*xyz,0))  # Four-byte-aligned vertex stride.
        if sys.byteorder != 'little':
            packed.byteswap()
        views[view_index] = packed.tobytes()
        normal_views.add(view_index)
        accessor['componentType'] = 5122
        accessor['normalized'] = True
        accessor.pop('min',None)
        accessor.pop('max',None)
    if normal_views:
        for key in ('extensionsUsed','extensionsRequired'):
            extensions = doc.setdefault(key,[])
            if 'KHR_mesh_quantization' not in extensions:
                extensions.append('KHR_mesh_quantization')
    rebuilt = bytearray()
    for i,view in enumerate(doc['bufferViews']):
        rebuilt.extend(b'\0'*(-len(rebuilt)%4))
        old_offset = view.get('byteOffset',0)
        payload = views.get(i,binary[old_offset:old_offset+view['byteLength']])
        view['byteOffset'] = len(rebuilt)
        view['byteLength'] = len(payload)
        if i in views:
            if i in normal_views:
                view['byteStride'] = 8
            else:
                view.pop('byteStride',None)
        rebuilt.extend(payload)
    doc['buffers'][0]['byteLength'] = len(rebuilt)
    rebuilt.extend(b'\0'*(-len(rebuilt)%4))
    header = json.dumps(doc,separators=(',',':')).encode('utf-8')
    header += b' '*(-len(header)%4)
    length = 12+8+len(header)+8+len(rebuilt)
    output = (struct.pack('<III',magic,version,length)+struct.pack('<II',len(header),0x4E4F534A)
              +header+struct.pack('<II',len(rebuilt),0x004E4942)+rebuilt)
    Path(destination or path).write_bytes(output)
    print(f'GLB attributes compacted: {len(data)} -> {len(output)} bytes; {len(views)} buffers',flush=True)
    return len(output)

if __name__ == '__main__':
    compact_attributes(sys.argv[1],sys.argv[2] if len(sys.argv)>2 else None)
