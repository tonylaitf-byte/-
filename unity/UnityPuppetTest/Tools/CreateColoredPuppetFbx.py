import bpy
import sys
from collections import Counter


args = sys.argv[sys.argv.index('--') + 1:]
if len(args) != 2:
    raise RuntimeError('Expected input FBX and output FBX paths after --')

source_path, output_path = args

# Blender starts with a default scene in background mode.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=source_path)


def principled_material(name, color, metallic, roughness):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    node = material.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color, 1.0)
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Roughness'].default_value = roughness
    material.diffuse_color = (*color, 1.0)
    return material


# A compact palette based on the provided glove-puppet reference.  Keeping the
# palette to five materials keeps the character readable from the game camera.
robe = principled_material('Puppet_Robe_Indigo', (0.025, 0.09, 0.27), 0.08, 0.42)
gold = principled_material('Puppet_Embroidery_Gold', (0.88, 0.53, 0.10), 0.80, 0.24)
skin = principled_material('Puppet_Face_Porcelain', (0.84, 0.66, 0.53), 0.0, 0.46)
hair = principled_material('Puppet_Hair_White', (0.82, 0.86, 0.91), 0.10, 0.30)
steel = principled_material('Puppet_Sword_ColdSteel', (0.34, 0.49, 0.66), 0.92, 0.16)


def dominant_group_name(obj, polygon):
    counts = Counter()
    for vertex_index in polygon.vertices:
        vertex = obj.data.vertices[vertex_index]
        for assignment in vertex.groups:
            if assignment.weight > 0.15:
                counts[obj.vertex_groups[assignment.group].name] += assignment.weight
    return counts.most_common(1)[0][0] if counts else ''


def assign_character_materials(obj):
    mesh = obj.data
    mesh.materials.clear()
    for material in (robe, gold, skin, hair):
        mesh.materials.append(material)

    for polygon in mesh.polygons:
        point = obj.matrix_world @ polygon.center
        group = dominant_group_name(obj, polygon)
        x, y, z = point.x, point.y, point.z
        # Do not infer a head from height: raised sleeves may be higher than
        # the shoulders.  The rig's Head/neck weights are the reliable split.
        is_head = group in {'Head', 'neck'}
        is_hand = group in {'LeftHand', 'RightHand'}

        # In this model the negative Y side faces the camera. This isolates the
        # facial area while the surrounding head volume becomes white hair.
        if is_head:
            # The face is the central, lower portion of the head volume.  Use
            # the x/z mask rather than camera-facing Y only, so its porcelain
            # colour remains stable when the head turns.
            polygon.material_index = 2 if abs(x) < 0.20 and 1.18 < z < 1.68 else 3
        elif is_hand:
            polygon.material_index = 2
        # A narrow front sash receives gold; most of the robe remains indigo.
        elif y < -0.10 and abs(x) < 0.16 and 0.05 < z < 1.25:
            polygon.material_index = 1
        else:
            polygon.material_index = 0


def ensure_sword_uv(obj):
    if obj.data.uv_layers:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15192, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)


for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    if obj.name == 'char1':
        assign_character_materials(obj)
        print(f'CHARACTER {obj.name}: UV={len(obj.data.uv_layers)} slots={len(obj.material_slots)}')
    elif obj.name.startswith('Mesh1'):
        ensure_sword_uv(obj)
        obj.data.materials.clear()
        obj.data.materials.append(steel)
        for polygon in obj.data.polygons:
            polygon.material_index = 0
        print(f'SWORD {obj.name}: UV={len(obj.data.uv_layers)} slots={len(obj.material_slots)}')

bpy.ops.export_scene.fbx(
    filepath=output_path,
    use_selection=False,
    add_leaf_bones=False,
    bake_anim=True,
    path_mode='AUTO',
    embed_textures=False,
    use_mesh_modifiers=True,
)
print('EXPORTED ' + output_path)
