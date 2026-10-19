import bpy
import sys


args = sys.argv[sys.argv.index('--') + 1:]
if len(args) != 1:
    raise RuntimeError('Expected output FBX path after --')
output_path = args[0]

character = bpy.data.objects.get('char1')
sword = bpy.data.objects.get('Mesh1.0')
wine_jug = bpy.data.objects.get('Mesh1.001')
if character is None or sword is None or wine_jug is None:
    raise RuntimeError('test.blend must contain char1, Mesh1.0 and Mesh1.001')

armature = None
for modifier in character.modifiers:
    if modifier.type == 'ARMATURE' and modifier.object is not None:
        armature = modifier.object
        break
if armature is None:
    armature = next((obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE'), None)
if armature is None:
    raise RuntimeError('Could not find the puppet armature')

# Keep the artist-authored face/hand assignments, but lift the skin value for
# Unity's darker moonlit lighting.  It stays warm enough to sit beside the
# blue robe and gold ornaments instead of reading as flat white plastic.
skin = bpy.data.materials.get('Skin')
if skin is not None:
    skin_color = (0.90, 0.57, 0.40, 1.0)
    skin.diffuse_color = skin_color
    if skin.use_nodes:
        principled = skin.node_tree.nodes.get('Principled BSDF')
        if principled is not None:
            principled.inputs['Base Color'].default_value = skin_color
            principled.inputs['Roughness'].default_value = 0.42

# Mesh1.001 is the artist-placed right-hand wine jug.  Its existing bone
# parent is preserved on export; give it a lacquered red ceramic finish that
# remains legible in the moonlit Unity lighting.
wine_jug.data.materials.clear()
jug_material = bpy.data.materials.get('Jug_Lacquered_Red')
if jug_material is None:
    jug_material = bpy.data.materials.new('Jug_Lacquered_Red')
jug_material.use_nodes = True
jug_shader = jug_material.node_tree.nodes.get('Principled BSDF')
jug_color = (0.34, 0.018, 0.012, 1.0)
jug_material.diffuse_color = jug_color
jug_shader.inputs['Base Color'].default_value = jug_color
jug_shader.inputs['Metallic'].default_value = 0.28
jug_shader.inputs['Roughness'].default_value = 0.24
wine_jug.data.materials.append(jug_material)

# The user's char1 material indices and all assigned polygons are left intact.
# Mesh1.001 is a hand-held wine jug.  It is exported with its original
# Armature/RightHand bone parent so it follows the live right-hand IK.
if not sword.data.uv_layers:
    bpy.context.view_layer.objects.active = sword
    sword.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15192, island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    sword.select_set(False)

if not sword.data.materials:
    sword_material = bpy.data.materials.new('Sword_ColdSteel')
    sword_material.diffuse_color = (0.34, 0.49, 0.66, 1.0)
    sword_material.use_nodes = True
    principled = sword_material.node_tree.nodes.get('Principled BSDF')
    principled.inputs['Base Color'].default_value = (0.34, 0.49, 0.66, 1.0)
    principled.inputs['Metallic'].default_value = 0.92
    principled.inputs['Roughness'].default_value = 0.16
    sword.data.materials.append(sword_material)

bpy.ops.object.select_all(action='DESELECT')
for obj in (armature, character, sword, wine_jug):
    obj.select_set(True)
bpy.context.view_layer.objects.active = armature

bpy.ops.export_scene.fbx(
    filepath=output_path,
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=True,
    path_mode='AUTO',
    embed_textures=False,
    use_mesh_modifiers=True,
)
print(f'EXPORTED_TEST_BLEND_COLOURS {output_path}')
