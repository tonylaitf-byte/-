import json
import sys
from pathlib import Path

import bpy


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    if len(args) < 2:
        raise SystemExit("Usage: blender --background --python verify_blender_fbx.py -- MODEL REPORT [require-rig]")

    model_path = Path(args[0]).resolve()
    report_path = Path(args[1]).resolve()
    require_rig = len(args) >= 3 and args[2].lower() == "require-rig"

    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = bpy.ops.import_scene.fbx(filepath=str(model_path), use_anim=True)

    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    bone_names = sorted(
        {
            bone.name
            for armature in armatures
            for bone in armature.data.bones
        }
    )
    vertex_count = sum(len(obj.data.vertices) for obj in meshes)
    polygon_count = sum(len(obj.data.polygons) for obj in meshes)
    material_names = sorted(material.name for material in bpy.data.materials)
    action_names = sorted(action.name for action in bpy.data.actions)

    success = "FINISHED" in result and len(meshes) > 0
    if require_rig:
        success = success and len(armatures) > 0 and len(bone_names) > 0

    report = {
        "success": success,
        "model": str(model_path),
        "requireRig": require_rig,
        "objectCount": len(bpy.data.objects),
        "meshCount": len(meshes),
        "armatureCount": len(armatures),
        "boneCount": len(bone_names),
        "vertexCount": vertex_count,
        "polygonCount": polygon_count,
        "materialCount": len(material_names),
        "actionCount": len(action_names),
        "armatures": [obj.name for obj in armatures],
        "bones": bone_names,
        "materials": material_names,
        "actions": action_names,
    }

    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False))
    raise SystemExit(0 if success else 2)


if __name__ == "__main__":
    main()
