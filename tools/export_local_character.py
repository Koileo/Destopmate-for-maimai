"""Export a decoded local Desktop Mate model into a version-independent mesh file.

The result contains geometry, rig, morphs and textures; it does not embed Unity 6
scripts, shaders, or the original animation controller. Keep the result local.
"""
import json
import struct
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / '.tools' / 'python'))
import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

bundle_path, original_path, out_path = map(Path, sys.argv[1:4])
out_path.mkdir(parents=True, exist_ok=True)
env = UnityPy.load(str(bundle_path))
objects = {o.path_id: o for o in env.objects}
nodes = [o for o in env.objects if o.type.name == 'Transform']
node_ids = {o.path_id: i for i, o in enumerate(nodes)}
go_nodes = {o.read().m_GameObject.path_id: i for i, o in enumerate(nodes)}
materials = [o for o in env.objects if o.type.name == 'Material']
material_ids = {o.path_id: i for i, o in enumerate(materials)}
def active_node(index):
    transform = nodes[index].read()
    if not transform.m_GameObject.read().m_IsActive:
        return False
    parent = node_ids.get(transform.m_Father.path_id)
    return parent is None or active_node(parent)


all_renderers = [o for o in env.objects if o.type.name == 'SkinnedMeshRenderer']
renderers = [o for o in all_renderers if o.read().m_Enabled
             and o.read().m_GameObject.read().m_Name != 'HairShadow'
             and active_node(go_nodes[o.read().m_GameObject.path_id])]
if not renderers:
    raise ValueError('No active skinned meshes found')
raw = original_path.read_bytes()
mesh_key = raw[264:268]
keys = (root / '.tools' / 'asset-converter' / 'keys.bin').read_bytes()
k0, = struct.unpack_from('<I', keys, (mesh_key[0] ^ mesh_key[1] ^ mesh_key[2] ^ mesh_key[3]) * 4)
k1, = struct.unpack_from('<I', keys, mesh_key[0] * 4 + 0x400)
final_keys = [int.from_bytes(mesh_key[:2], 'little') ^ (k1 & 0xffff),
              int.from_bytes(mesh_key[2:], 'little') ^ (k1 >> 16),
              int.from_bytes(mesh_key[1:3], 'little') ^ (k0 & 0xffff),
              ((mesh_key[1] << 8) | mesh_key[3]) ^ (k0 >> 16)]

f = (out_path / 'character.dmc').open('wb')
def ints(*v): f.write(struct.pack('<' + 'i' * len(v), *v))
def floats(*v): f.write(struct.pack('<' + 'f' * len(v), *v))
def string(s):
    data = s.encode('utf-8')
    ints(len(data))
    f.write(data)
def vec(v, components='xyz'): floats(*(v[k] for k in components))


def channel_array(handler, tree, name, channel):
    values = getattr(handler, name)
    array = np.asarray(values if values is not None else [], np.float32)
    info = tree['m_VertexData']['m_Channels'][channel]
    # UnityPy returns packed normalized integers without converting their range.
    # Apply Unity's UNorm/SNorm semantics to every channel that uses them.
    if info['dimension'] and len(array):
        divisor = {2: 255.0, 3: 127.0, 4: 65535.0, 5: 32767.0}.get(info['format'])
        if divisor:
            array = array / divisor
            if info['format'] in (3, 5):
                array = np.maximum(array, -1.0)
    return array

f.write(b'DMCHAR01')
ints(len(nodes))
for obj in nodes:
    t = obj.read_typetree()
    string(objects[t['m_GameObject']['m_PathID']].read().m_Name)
    ints(node_ids.get(t['m_Father']['m_PathID'], -1))
    vec(t['m_LocalPosition'])
    vec(t['m_LocalRotation'], 'xyzw')
    vec(t['m_LocalScale'])

ints(len(materials))
for obj in materials:
    t = obj.read_typetree()
    props = t['m_SavedProperties']
    textures = dict(props['m_TexEnvs'])
    values = dict(props['m_Floats'])
    colors = dict(props['m_Colors'])
    main = next((textures.get(name, {}).get('m_Texture', {}).get('m_PathID', 0)
                 for name in ('_MainTex', '_BaseMap', '_BaseColorMap')
                 if textures.get(name, {}).get('m_Texture', {}).get('m_PathID', 0)), 0)
    texture_file = ''
    if main:
        image = objects[main].read().image.convert('RGBA')
        alpha = textures.get('_AlphaMask', {}).get('m_Texture', {}).get('m_PathID', 0)
        if alpha:
            mask = objects[alpha].read().image.convert('L').resize(image.size)
            image.putalpha(mask)
        texture_file = 'texture_' + str(material_ids[obj.path_id]) + '.png'
        image.save(out_path / texture_file)
    string(t['m_Name'])
    string(texture_file)
    color = colors.get('_Color', dict(r=1, g=1, b=1, a=1))
    vec(color, 'rgba')
    ints(1 if values.get('_TransparentMode', 0) else 0)

ints(len(renderers))
for obj in renderers:
    renderer = obj.read_typetree()
    mesh_obj = objects[renderer['m_Mesh']['m_PathID']]
    mesh = mesh_obj.read()
    tree = mesh_obj.read_typetree()
    handler = MeshHandler(mesh)
    handler.process()
    positions = channel_array(handler, tree, 'm_Vertices', 0)[:, :3]
    normals = channel_array(handler, tree, 'm_Normals', 1)[:, :3]
    uv7 = channel_array(handler, tree, 'm_UV7', 11)
    # CBv3 additionally displaces vertices along their normals.
    if len(uv7) and not dict(materials[material_ids[renderer['m_Materials'][0]['m_PathID']]].read_typetree()['m_SavedProperties']['m_Floats']).get('_DmIgnoreEncryption', 0):
        x = (uv7[:, 0] * 256.0 + 0.5).astype(np.uint32)
        for shift, multiplier, key in zip([16, 13, 16, 13], [0x7FEB352D, 0xC2B2AE35, 0x6B2B79A9, 0xB5AD4EC3], final_keys):
            x = ((x ^ (x >> shift)) * np.uint32(multiplier)) ^ np.uint32(key)
        x = ((x ^ (x >> 16)) * np.uint32(0x1B873593)) & 255
        positions -= normals * (x.astype(np.float32) / 256.0)[:, None]
    print(renderer['m_GameObject']['m_PathID'], len(positions), positions.min(axis=0), positions.max(axis=0))
    string(objects[renderer['m_GameObject']['m_PathID']].read().m_Name)
    ints(go_nodes[renderer['m_GameObject']['m_PathID']])
    bones, poses = renderer['m_Bones'], tree['m_BindPose']
    if not bones:
        # A rigid SkinnedMeshRenderer uses its own transform as an identity bone.
        bones = [{'m_PathID': nodes[go_nodes[renderer['m_GameObject']['m_PathID']]].path_id}]
        poses = [{f'e{r}{c}': float(r == c) for r in range(4) for c in range(4)}]
    if len(bones) != len(poses):
        raise ValueError('Bone/bind pose count mismatch')
    ints(len(bones))
    for bone, pose in zip(bones, poses):
        ints(node_ids[bone['m_PathID']])
        floats(*(pose[f'e{r}{c}'] for r in range(4) for c in range(4)))
    ints(len(positions))
    uvs = channel_array(handler, tree, 'm_UV0', 4)
    if handler.m_BoneIndices is None:
        if len(bones) != 1:
            raise ValueError('Missing indices on a mesh with multiple bones')
        indices = np.zeros((len(positions), 1), np.int32)
    else:
        indices = np.asarray(handler.m_BoneIndices, np.int32).reshape(len(positions), -1)
    if handler.m_BoneWeights is None:
        # Unity stores single-influence vertices as an index without weights.
        if indices.shape[1] != 1:
            raise ValueError('Missing weights on a mesh with multiple influences')
        weights = np.ones((len(positions), 1), np.float32)
    else:
        weights = channel_array(handler, tree, 'm_BoneWeights', 12).reshape(len(positions), -1)
    for i in range(len(positions)):
        floats(*positions[i], *normals[i], *uvs[i][:2])
        bone_indices = list(indices[i][:4])
        bone_weights = list(weights[i][:4])
        ints(*(bone_indices + [0] * (4 - len(bone_indices))))
        floats(*(bone_weights + [0.0] * (4 - len(bone_weights))))
    triangles = handler.get_triangles()
    ints(len(triangles))
    for index, triangles_for_material in enumerate(triangles):
        ints(material_ids[renderer['m_Materials'][index]['m_PathID']])
        triangle_array = np.asarray(triangles_for_material, dtype='<i4').reshape(-1)
        ints(len(triangle_array))
        f.write(triangle_array.tobytes())
    shapes = tree['m_Shapes']
    ints(len(shapes['channels']))
    for channel in shapes['channels']:
        string(channel['name'])
        shape = shapes['shapes'][channel['frameIndex'] + channel['frameCount'] - 1]
        start, count = shape['firstVertex'], shape['vertexCount']
        ints(count)
        for vertex in shapes['vertices'][start:start+count]:
            ints(vertex['index'])
            vec(vertex['vertex'])
            vec(vertex['normal'])
            vec(vertex['tangent'])
f.close()
(out_path / 'source.json').write_text(json.dumps(dict(
    source=str(original_path), character=original_path.stem,
    renderer='Original local geometry and rig; built-in unlit material approximation',
    animations='Procedural idle, blink and gaze. Original controller not ported.'), ensure_ascii=False, indent=2), encoding='utf-8')
print('Exported:', out_path / 'character.dmc')
