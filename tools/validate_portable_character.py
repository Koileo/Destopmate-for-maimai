"""Validate the exported interchange independently of Unity before installation."""
import math
import struct
import sys
from pathlib import Path

path = Path(sys.argv[1])
f = path.open('rb')
def read(n):
    data = f.read(n)
    if len(data) != n: raise ValueError(f'Truncated file at {f.tell()}')
    return data
def integer(): return struct.unpack('<i', read(4))[0]
def count(maximum):
    value = integer()
    assert 0 <= value <= maximum, (f.tell()-4, value, maximum)
    return value
def text(): return read(count(4096)).decode('utf-8')
def floats(n):
    values = struct.unpack('<' + 'f'*n, read(n*4))
    assert all(math.isfinite(v) for v in values)
    return values
assert read(8) == b'DMCHAR01'
node_count = count(4096)
assert node_count > 0, 'Character has no nodes'
for _ in range(node_count):
    text(); parent = integer(); assert -1 <= parent < node_count
    floats(10)
material_count = count(256)
for _ in range(material_count):
    text(); texture = text()
    if texture: assert (path.parent / texture).is_file(), texture
    floats(4); integer()
mesh_count = count(128)
assert mesh_count > 0, 'Character has no meshes'
vertex_total = triangle_total = morph_total = 0
for _ in range(mesh_count):
    name = text(); node = integer(); assert 0 <= node < node_count
    bone_count = count(4096)
    for _ in range(bone_count):
        node = integer(); assert 0 <= node < node_count
        floats(16)
    vertices = count(200000)
    vertex_total += vertices
    for _ in range(vertices):
        floats(8)
        bones = struct.unpack('<4i', read(16))
        weights = floats(4)
        assert all(0 <= b < bone_count for b in bones), (name, bones)
        assert abs(sum(weights) - 1) < 0.002, (name, weights)
    for _ in range(count(256)):
        material = integer(); assert 0 <= material < material_count
        indices = count(2000000)
        assert indices % 3 == 0
        values = struct.unpack('<'+'i'*indices, read(indices*4))
        assert all(0 <= i < vertices for i in values)
        triangle_total += indices // 3
    shapes = count(256)
    morph_total += shapes
    for _ in range(shapes):
        text()
        for _ in range(count(vertices)):
            index = integer(); assert 0 <= index < vertices
            floats(9)
assert f.tell() == path.stat().st_size, (f.tell(), path.stat().st_size)
print(f'PASS: {node_count} nodes, {mesh_count} meshes, {vertex_total} vertices, '
      f'{triangle_total} triangles, {morph_total} morphs; indices, weights, textures and file length valid')
