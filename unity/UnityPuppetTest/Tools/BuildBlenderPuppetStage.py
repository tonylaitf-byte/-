import bpy
import math
import os
import sys
from mathutils import Vector


args = sys.argv[sys.argv.index('--') + 1:]
if len(args) != 5:
    raise RuntimeError('Expected architecture FBX, puppet FBX, backdrop image, output blend and preview PNG.')
architecture_path, puppet_path, backdrop_path, output_blend, preview_path = map(os.path.abspath, args)


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for data in (bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in data:
            data.remove(item)


def imported_objects(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [obj for obj in bpy.data.objects if obj not in before]


def bounds(objects):
    points = []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    if not points:
        return Vector((-1, -1, -1)), Vector((1, 1, 1))
    return (
        Vector(tuple(min(point[index] for point in points) for index in range(3))),
        Vector(tuple(max(point[index] for point in points) for index in range(3))),
    )


def material(name, color, metallic=0.0, roughness=0.5):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    shader = result.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1.0)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    return result


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()


clear_scene()
architecture = imported_objects(architecture_path)
puppet = imported_objects(puppet_path)

wood = material('Stage_Wood', (0.045, 0.025, 0.014), 0.0, 0.55)
roof = material('Stage_Roof', (0.03, 0.065, 0.11), 0.08, 0.38)
stone = material('Stage_Stone', (0.10, 0.12, 0.15), 0.0, 0.72)
for obj in architecture:
    if obj.type != 'MESH':
        continue
    lower = obj.name.lower()
    selected = roof if 'roof' in lower else stone if any(word in lower for word in ('stone', 'floor', 'ground')) else wood
    if len(obj.data.materials) == 0:
        obj.data.materials.append(selected)

arch_min, arch_max = bounds(architecture)
puppet_min, puppet_max = bounds(puppet)
arch_center = (arch_min + arch_max) * 0.5

# Move every puppet root to the centre of the imported courtyard and have the
# lowest part of the costume rest at the architecture's ground height.
offset = Vector((arch_center.x - (puppet_min.x + puppet_max.x) * 0.5,
                 arch_center.y - (puppet_min.y + puppet_max.y) * 0.5,
                 arch_min.z - puppet_min.z))
puppet_roots = [obj for obj in puppet if obj.parent is None]
for root in puppet_roots:
    root.location += offset

puppet_min, puppet_max = bounds(puppet)
puppet_center = (puppet_min + puppet_max) * 0.5

# Moonlit backplate behind the temple.
if os.path.exists(backdrop_path):
    bpy.ops.mesh.primitive_plane_add(size=2, location=(arch_center.x, arch_max.y + 0.6, arch_center.z + (arch_max.z - arch_min.z) * 0.45))
    backplate = bpy.context.object
    backplate.name = 'Moonlit Mountain Backplate'
    backplate.rotation_euler = (math.radians(90), 0, 0)
    backplate.scale = ((arch_max.x - arch_min.x) * 0.75, (arch_max.z - arch_min.z) * 0.45, 1)
    backdrop = bpy.data.materials.new('Moonlit_Backdrop')
    backdrop.use_nodes = True
    nodes = backdrop.node_tree.nodes
    links = backdrop.node_tree.links
    shader = nodes.get('Principled BSDF')
    image = bpy.data.images.load(backdrop_path, check_existing=True)
    texture = nodes.new('ShaderNodeTexImage')
    texture.image = image
    links.new(texture.outputs['Color'], shader.inputs['Base Color'])
    shader.inputs['Roughness'].default_value = 0.9
    backplate.data.materials.append(backdrop)

# Camera and a compact moonlit three-point lighting setup.
bpy.ops.object.camera_add(location=(puppet_center.x, puppet_min.y - max(5.0, (arch_max.x - arch_min.x) * 0.55), puppet_center.z + 0.35))
camera = bpy.context.object
camera.name = 'Stage Camera'
camera.data.lens = 52
look_at(camera, (puppet_center.x, puppet_center.y, puppet_center.z + 0.05))
bpy.context.scene.camera = camera

for name, location, color, energy, size in (
    ('Moon Key', (puppet_center.x - 3.0, puppet_center.y - 3.0, puppet_center.z + 4.0), (0.35, 0.50, 1.0), 950, 3.0),
    ('Lantern Fill', (puppet_center.x + 2.4, puppet_center.y - 1.3, puppet_center.z + 1.3), (1.0, 0.34, 0.12), 400, 1.2),
    ('Rim Light', (puppet_center.x, puppet_center.y + 2.0, puppet_center.z + 3.0), (0.28, 0.42, 1.0), 650, 2.0),
):
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.name = name
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.data.color = color
    look_at(light, puppet_center)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 960
scene.render.resolution_y = 540
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = preview_path
scene.world.color = (0.005, 0.008, 0.02)
scene.render.film_transparent = False

os.makedirs(os.path.dirname(output_blend), exist_ok=True)
os.makedirs(os.path.dirname(preview_path), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=output_blend)
bpy.ops.render.render(write_still=True)
print(f'BLENDER_STAGE_SAVED {output_blend}')
print(f'BLENDER_STAGE_PREVIEW {preview_path}')
