import bpy
from collections import Counter
from mathutils import Vector


for obj in bpy.context.scene.objects:
    if obj.type == 'ARMATURE':
        print(f"ARMATURE name={obj.name} bones={[bone.name for bone in obj.data.bones]}")
    if obj.type != 'MESH':
        continue

    slots = [slot.material.name if slot.material else '<empty>' for slot in obj.material_slots]
    usage = Counter(poly.material_index for poly in obj.data.polygons)
    world_points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = tuple(round(min(point[index] for point in world_points), 4) for index in range(3))
    maximum = tuple(round(max(point[index] for point in world_points), 4) for index in range(3))
    print(
        f"MESH name={obj.name} vertices={len(obj.data.vertices)} "
        f"polygons={len(obj.data.polygons)} uv={len(obj.data.uv_layers)} slots={slots} "
        f"usage={dict(usage)} parent={obj.parent.name if obj.parent else '<none>'} "
        f"parentType={obj.parent_type} parentBone={obj.parent_bone or '<none>'} worldBounds={minimum}..{maximum} "
        f"groups={[group.name for group in obj.vertex_groups]}"
    )
    for index, material in enumerate(obj.data.materials):
        if material is None:
            continue
        color = tuple(round(value, 4) for value in material.diffuse_color[:3])
        print(f"  MATERIAL index={index} name={material.name} diffuse={color}")
