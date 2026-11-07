"""Replace the render-only flat courtyard with worn, individually raised slabs."""
import bpy
import json
import math
import random
import sys
from pathlib import Path
from mathutils import Vector

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
scene = bpy.context.scene
original = bpy.data.objects.get('wet stone courtyard')
if original is None:
    raise RuntimeError('Expected courtyard mesh is missing.')
floor_z = max((original.matrix_world @ Vector(c)).z for c in original.bound_box)
camera_matrix = scene.camera.matrix_world.copy()
other_geometry = {o.name: (len(o.data.vertices), o.matrix_world.copy())
                  for o in scene.objects if o.type == 'MESH' and o != original}
print('INSPECT_FLOOR', original.name, 'top:', floor_z, 'vertices:', len(original.data.vertices))
original.hide_render = True
original.hide_viewport = True
random.seed(1107)

material = bpy.data.materials['Runtime Wet Courtyard Stone'].copy()
material.name = 'Worn uneven flagstone'
nodes, links = material.node_tree.nodes, material.node_tree.links
shader = nodes.get('Principled BSDF')
mix = shader.inputs['Base Color'].links[0].from_node
info = nodes.new('ShaderNodeObjectInfo')
variation = nodes.new('ShaderNodeValToRGB')
variation.name = 'Individual stone color'
variation.color_ramp.elements[0].color = (.020, .028, .040, 1)
variation.color_ramp.elements[1].color = (.060, .073, .088, 1)
links.new(info.outputs['Random'], variation.inputs[0])
links.new(variation.outputs['Color'], mix.inputs[1])
# Retain fine grain and wet patches, but remove the fake rectangular tile bump.
fine_bump = shader.inputs['Normal'].links[0].from_node
for link in list(fine_bump.inputs['Normal'].links):
    links.remove(link)
for node in list(nodes):
    if node.type == 'TEX_BRICK':
        nodes.remove(node)

base_material = bpy.data.materials.new('Dark earth between old stones')
base_material.use_nodes = True
base_shader = base_material.node_tree.nodes.get('Principled BSDF')
base_shader.inputs['Base Color'].default_value = (.006, .009, .012, 1)
base_shader.inputs['Roughness'].default_value = .95
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 2, floor_z - .18))
base = bpy.context.object
base.name = 'Courtyard stone bedding'
base.scale = (25, 29, .12)
base.data.materials.append(base_material)
collection = bpy.data.collections.new('Uneven old courtyard stones')
scene.collection.children.link(collection)


def protection(x, y):
    # Keep the complete feast, mats, pots and actor-foot zone at its old level.
    outside = max(abs(x) - 3.9, abs(y + .7) - 1.9, 0)
    t = min(1, outside / .9)
    return t * t * (3 - 2 * t)


rows = [-12.0]
while rows[-1] < 16:
    rows.append(min(16, rows[-1] + random.uniform(.8, 1.3)))
phases = [random.uniform(-math.pi, math.pi) for _ in rows]


def boundary(index, x):
    return rows[index] + .08 * math.sin(x * .83 + phases[index])


top_offsets = []
stones = 0
for row in range(len(rows) - 1):
    x = -12 - random.uniform(0, 1.2)
    while x < 12:
        width = random.uniform(.75, 1.65)
        left, right = max(-12, x) + .018, min(12, x + width) - .018
        x += width
        if right - left < .15:
            continue
        lo = boundary(row, (left + right) / 2) + .018
        hi = boundary(row + 1, (left + right) / 2) - .018
        cuts = [random.uniform(.04, .14) for _ in range(4)]
        points = [(left + cuts[0], lo), (right - cuts[1], lo),
                  (right, lo + cuts[1]), (right, hi - cuts[2]),
                  (right - cuts[2], hi), (left + cuts[3], hi),
                  (left, hi - cuts[3]), (left, lo + cuts[0])]
        raise_z = random.uniform(-.025, .04)
        tilt_x, tilt_y = random.uniform(-.016, .016), random.uniform(-.016, .016)
        center_x, center_y = (left + right) / 2, (lo + hi) / 2
        top = []
        for px, py in points:
            offset = (raise_z + (px - center_x) * tilt_x + (py - center_y) * tilt_y) * protection(px, py)
            top_offsets.append(offset)
            top.append((px, py, floor_z + offset))
        vertices = top + [(px, py, floor_z - .12) for px, py in points]
        center_z = sum(p[2] for p in top) / 8 + random.uniform(-.003, .003) * protection(center_x, center_y)
        vertices.append((center_x, center_y, center_z))
        faces = [(16, i, (i + 1) % 8) for i in range(8)]
        faces += [(i, i + 8, (i + 1) % 8 + 8, (i + 1) % 8) for i in range(8)]
        faces.append(tuple(reversed(range(8, 16))))
        mesh = bpy.data.meshes.new('Worn stone slab')
        mesh.from_pydata(vertices, [], faces); mesh.update()
        obj = bpy.data.objects.new('Old courtyard slab %04d' % stones, mesh)
        collection.objects.link(obj); mesh.materials.append(material)
        bevel = obj.modifiers.new('Worn chipped edges', 'BEVEL')
        bevel.width = .012; bevel.segments = 2
        bevel.limit_method = 'ANGLE'
        stones += 1

unchanged = all(o.name in other_geometry and len(o.data.vertices) == other_geometry[o.name][0]
                and o.matrix_world == other_geometry[o.name][1]
                for o in scene.objects if o.type == 'MESH'
                and o != original and o != base and o.name in other_geometry)
if not unchanged or scene.camera.matrix_world != camera_matrix:
    raise RuntimeError('Non-floor geometry or camera changed.')
try:
    preferences = bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type = 'OPTIX'; preferences.get_devices()
    for device in preferences.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(d.use for d in preferences.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'
scene.render.filepath = str(folder / 'blender-uneven-stones.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-uneven-stones.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'UnevenStones.json').write_text(json.dumps({
    'slabs': stones, 'originalFloorHeight': floor_z,
    'topOffsetRangeMeters': [min(top_offsets), max(top_offsets)],
    'protectedFootAndFeastZoneLevel': True,
    'otherGeometryUnchanged': unchanged, 'cameraUnchanged': True,
    'scope': 'Actual slab geometry with irregular edges, staggered sizes and elevation. Blender only; Unity unchanged.',
}, indent=2))
print('BLENDER_UNEVEN_STONES_PASS')
