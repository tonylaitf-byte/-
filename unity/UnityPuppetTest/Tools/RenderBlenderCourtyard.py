"""Render an exported production-scene snapshot without editing Unity assets."""
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
snapshot = json.loads((folder / 'scene-snapshot.json').read_text(encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def vector(value):
    return (value['x'], value['z'], value['y'])


def color(value):
    return (value['r'], value['g'], value['b'], value.get('a', 1))


materials = []
for surface in snapshot['materials']:
    mat = bpy.data.materials.new(surface['name'])
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = color(surface['color'])
    shader.inputs['Metallic'].default_value = surface['metallic']
    shader.inputs['Roughness'].default_value = max(.16, surface['roughness'])
    tex = None
    if surface['texture']:
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(surface['texture'], check_existing=True)
        uv = nodes.new('ShaderNodeTexCoord')
        mapping = nodes.new('ShaderNodeVectorMath'); mapping.operation = 'MULTIPLY_ADD'
        mapping.inputs[1].default_value = (surface['scale']['x'], surface['scale']['y'], 1)
        mapping.inputs[2].default_value = (surface['offset']['x'], surface['offset']['y'], 0)
        links.new(uv.outputs['UV'], mapping.inputs[0]); links.new(mapping.outputs[0], tex.inputs['Vector'])
        mix = nodes.new('ShaderNodeMixRGB'); mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1
        mix.inputs[2].default_value = color(surface['color'])
        links.new(tex.outputs['Color'], mix.inputs[1]); links.new(mix.outputs[0], shader.inputs['Base Color'])
        if surface['name'] == 'BanquetBackdrop':
            emission = nodes.new('ShaderNodeEmission')
            links.new(tex.outputs['Color'], emission.inputs['Color'])
            emission.inputs['Strength'].default_value = .7
            links.new(emission.outputs[0], nodes.get('Material Output').inputs['Surface'])
    if surface['normal']:
        normal = nodes.new('ShaderNodeTexImage')
        normal.image = bpy.data.images.load(surface['normal'], check_existing=True)
        normal.image.colorspace_settings.name = 'Non-Color'
        node = nodes.new('ShaderNodeNormalMap'); node.inputs['Strength'].default_value = .3
        links.new(normal.outputs['Color'], node.inputs['Color']); links.new(node.outputs['Normal'], shader.inputs['Normal'])
    elif any(word in surface['name'] for word in ('Stone', 'Roof', 'Wood', 'Silk', 'Royal_Blue')):
        noise = nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 140
        bump = nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = .16
        bump.inputs['Distance'].default_value = .006
        links.new(noise.outputs['Fac'], bump.inputs['Height']); links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    if surface['name'] == 'Runtime Wet Courtyard Stone':
        position = nodes.new('ShaderNodeNewGeometry')
        brick = nodes.new('ShaderNodeTexBrick')
        brick.inputs['Scale'].default_value = 1.2
        brick.inputs['Brick Width'].default_value = 1.25
        brick.inputs['Row Height'].default_value = .85
        brick.inputs['Mortar Size'].default_value = .012
        brick.inputs['Color1'].default_value = (.023, .031, .043, 1)
        brick.inputs['Color2'].default_value = (.052, .064, .077, 1)
        brick.inputs['Mortar'].default_value = (.006, .009, .013, 1)
        links.new(position.outputs['Position'], brick.inputs['Vector'])
        links.new(brick.outputs['Color'], shader.inputs['Base Color'])
        bump = nodes.new('ShaderNodeBump'); bump.invert = True
        bump.inputs['Strength'].default_value = .45; bump.inputs['Distance'].default_value = .04
        links.new(brick.outputs['Fac'], bump.inputs['Height']); links.new(bump.outputs['Normal'], shader.inputs['Normal'])
        shader.inputs['Roughness'].default_value = .3
    materials.append(mat)

for part in snapshot['parts']:
    faces, slots = [], []
    for sub, group in enumerate(part['triangles']):
        triangles = group['values']
        # The Y/Z coordinate swap reverses handedness.
        for i in range(0, len(triangles), 3):
            faces.append((triangles[i], triangles[i + 2], triangles[i + 1])); slots.append(sub)
    mesh = bpy.data.meshes.new(part['name'])
    mesh.from_pydata([vector(v) for v in part['vertices']], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(part['name'], mesh); scene.collection.objects.link(obj)
    for index in part['materials']:
        mesh.materials.append(materials[index])
    if len(part['uv']) == len(part['vertices']):
        uv = mesh.uv_layers.new()
        for loop in mesh.loops:
            point = part['uv'][loop.vertex_index]; uv.data[loop.index].uv = (point['x'], point['y'])
    for poly, slot in zip(mesh.polygons, slots):
        poly.material_index = min(slot, len(part['materials']) - 1)
        poly.use_smooth = True
    if len(part.get('normals', [])) == len(part['vertices']):
        mesh.normals_split_custom_set_from_vertices([vector(n) for n in part['normals']])
    if 'Backdrop' in part['name']:
        obj.visible_shadow = False
    # Preserve authored geometry; only add tiny edge highlights to architecture.
    if any(word in part['name'].lower() for word in ('stone', 'step', 'hall body')):
        bevel = obj.modifiers.new('Fine stone edges', 'BEVEL'); bevel.width = .008; bevel.segments = 2
        bevel.limit_method = 'ANGLE'


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


bpy.ops.object.camera_add(location=vector(snapshot['cameraPosition']))
camera = bpy.context.object; camera.name = 'Production front camera'
aim(camera, camera.location + Vector(vector(snapshot['cameraForward'])))
camera.data.sensor_fit = 'VERTICAL'; camera.data.sensor_height = 24
camera.data.lens = 12 / math.tan(math.radians(snapshot['fieldOfView']) / 2)
camera.data.clip_end = 200
scene.camera = camera


def area(name, location, target, energy, tint, size):
    data = bpy.data.lights.new(name, 'AREA'); data.energy = energy; data.color = tint; data.shape = 'DISK'; data.size = size
    obj = bpy.data.objects.new(name, data); scene.collection.objects.link(obj); obj.location = location; aim(obj, target)


area('Cool moon softbox', (-4, 1, 7), (0, 2, 0), 1000, (.56, .69, 1), 4)
area('Soft face fill', (0, -5, 3), (0, 0, .9), 300, (.85, .88, 1), 5)
area('Warm courtyard bounce', (4, -1, 3), (0, 1, .5), 350, (1, .58, .29), 3)
area('Temple rim', (0, 5, 5), (0, 0, 1), 800, (.46, .64, 1), 3)
for x in (-2.45, 2.45):
    data = bpy.data.lights.new('Warm floor lantern', 'POINT'); data.energy = 22; data.color = (1, .38, .1); data.shadow_soft_size = .12
    obj = bpy.data.objects.new('Warm floor lantern', data); scene.collection.objects.link(obj); obj.location = (x, -1.74, -.25)

# Thin localized haze behind the actors, leaving faces and feast unobscured.
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 7, 2))
fog = bpy.context.object; fog.name = 'Courtyard atmospheric haze'; fog.scale = (22, 16, 6)
mist = bpy.data.materials.new('Subtle mountain air'); mist.use_nodes = True
mist.node_tree.nodes.clear(); output = mist.node_tree.nodes.new('ShaderNodeOutputMaterial')
volume = mist.node_tree.nodes.new('ShaderNodeVolumePrincipled'); volume.inputs['Density'].default_value = .004
volume.inputs['Color'].default_value = (.48, .60, .75, 1); volume.inputs['Anisotropy'].default_value = .25
mist.node_tree.links.new(volume.outputs['Volume'], output.inputs['Volume']); fog.data.materials.append(mist)
scene.world = bpy.data.worlds.new('Moonlit ambient world')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.16, .22, .36, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .18
scene.render.engine = 'CYCLES'; scene.cycles.samples = 96; scene.cycles.use_denoising = True
scene.cycles.adaptive_threshold = .04
device = 'CPU'
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'; prefs.get_devices()
    gpu = [d for d in prefs.devices if d.type == 'OPTIX']
    if gpu:
        for d in prefs.devices: d.use = d.type == 'OPTIX'
        scene.cycles.device = 'GPU'; device = 'OPTIX'
except Exception as error:
    print('GPU_FALLBACK', str(error))
scene.render.resolution_x = 1600; scene.render.resolution_y = 900; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.filepath = str(folder / 'blender-cycles.png')
scene.view_settings.view_transform = 'AgX'; scene.view_settings.exposure = 0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-cycles.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'Render.json').write_text(json.dumps({'engine':'Cycles', 'samples':96, 'device':device,
    'resolution':[1600,900], 'objects':len(snapshot['parts']), 'materials':len(materials),
    'scope':'Static posed production-scene geometry with Blender PBR approximations and new offline lighting; not a live rig or pixel-identical Unity shader conversion.'}, indent=2))
print('BLENDER_COURTYARD_RENDER_PASS')
