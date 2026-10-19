import bpy
import sys


path = sys.argv[sys.argv.index('--') + 1]
bpy.ops.import_scene.fbx(filepath=path)
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    slots = [slot.material.name if slot.material else '<empty>' for slot in obj.material_slots]
    print(
        f"EXPORTED_MESH name={obj.name} parent={obj.parent.name if obj.parent else '<none>'} "
        f"parentType={obj.parent_type} parentBone={obj.parent_bone or '<none>'} materials={slots}"
    )
