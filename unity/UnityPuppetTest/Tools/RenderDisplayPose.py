"""Replace only static actor snapshots in the approved Blender render scene."""
import bpy
import json
import re
import sys
from pathlib import Path

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
data = json.loads((folder / 'display-pose-snapshot.json').read_text(encoding='utf-8'))
scene = bpy.context.scene
camera = scene.camera.matrix_world.copy()
names = set(data['actorObjectNames'])


def is_actor(name):
    return name in names or re.sub(r'\.\d{3}$', '', name) in names


original = {o.name: (len(o.data.vertices), o.matrix_world.copy())
            for o in scene.objects if o.type == 'MESH' and not is_actor(o.name)}
removed = []
for obj in list(scene.objects):
    if obj.type == 'MESH' and is_actor(obj.name):
        removed.append(obj.name)
        bpy.data.objects.remove(obj, do_unlink=True)
if not removed:
    raise RuntimeError('No existing actors matched the rig snapshot.')
print('INSPECT_ACTORS', 'removed:', len(removed), 'new:', len(data['parts']))


def vector(v):
    return (v['x'], v['z'], v['y'])


for part in data['parts']:
    faces, slots = [], []
    for sub, group in enumerate(part['triangles']):
        indices = group['values']
        for i in range(0, len(indices), 3):
            faces.append((indices[i], indices[i + 2], indices[i + 1]))
            slots.append(sub)
    mesh = bpy.data.meshes.new(part['name'] + ' display mesh')
    mesh.from_pydata([vector(v) for v in part['vertices']], [], faces)
    mesh.update()
    for index in part['materials']:
        source = data['materials'][index]
        material = bpy.data.materials.get(source['name'])
        if not material:
            material = bpy.data.materials.get(source['name'].removesuffix(' (Instance)'))
        if not material:
            # Blender can purge an unused first instance while keeping .001.
            material = next((m for m in bpy.data.materials if m.name.startswith(source['name'] + '.')), None)
        if not material:
            raise RuntimeError('Existing material missing: ' + source['name'])
        mesh.materials.append(material)
    if len(part['uv']) == len(part['vertices']):
        uv = mesh.uv_layers.new()
        for loop in mesh.loops:
            point = part['uv'][loop.vertex_index]
            uv.data[loop.index].uv = (point['x'], point['y'])
    for face, slot in zip(mesh.polygons, slots):
        face.material_index = min(slot, len(part['materials']) - 1)
        face.use_smooth = True
    if len(part['normals']) == len(part['vertices']):
        mesh.normals_split_custom_set_from_vertices([vector(n) for n in part['normals']])
    obj = bpy.data.objects.new(part['name'], mesh)
    scene.collection.objects.link(obj)

for name, (count, matrix) in original.items():
    obj = scene.objects.get(name)
    if obj is None or len(obj.data.vertices) != count or obj.matrix_world != matrix:
        raise RuntimeError('Non-actor geometry changed: ' + name)
if scene.camera.matrix_world != camera:
    raise RuntimeError('Camera changed unexpectedly.')
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'; prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(d.use for d in prefs.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'
scene.render.filepath = str(folder / 'blender-display-pose.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-display-pose.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'DisplayPose.json').write_text(json.dumps({
    'replacedActorMeshes': len(removed), 'newActorMeshes': len(data['parts']),
    'cameraUnchanged': True, 'nonActorGeometryUnchanged': True,
    'exposure': scene.view_settings.exposure,
    'poses': {'hero': 'Existing ALERT action at 1.5 seconds over relaxed mapping input',
              'musician': 'Existing LANDSCAPE / slow-playing action at 1.5 seconds'},
    'scope': 'Static Blender showcase only. Production rig, controller settings, scenes and WebGL unchanged.',
}, indent=2))
print('BLENDER_DISPLAY_POSE_PASS')
