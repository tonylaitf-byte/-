"""Local character accents and irregular wet stone, preserving the approved night."""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
scene = bpy.context.scene
camera_matrix = scene.camera.matrix_world.copy()
geometry = {obj.name: (len(obj.data.vertices), obj.matrix_world.copy())
            for obj in scene.objects if obj.type == 'MESH'}
floor = bpy.data.materials.get('Runtime Wet Courtyard Stone')
if floor is None:
    raise RuntimeError('Approved stone floor material is missing.')
print('INSPECT_BEFORE', len(geometry), 'meshes; floor:', floor.name,
      'camera:', scene.camera.name, 'exposure:', scene.view_settings.exposure)

try:
    preferences = bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type = 'OPTIX'
    preferences.get_devices()
    for device in preferences.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(d.use for d in preferences.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'


def accent(name, target, energy, cone):
    light = bpy.data.lights.new(name, 'SPOT')
    light.energy = energy
    light.color = (.72, .82, 1)
    light.spot_size = cone
    light.spot_blend = .85
    light.shadow_soft_size = .22
    obj = bpy.data.objects.new(name, light)
    scene.collection.objects.link(obj)
    obj.location = Vector(target) + Vector((-.15, -1.8, .65))
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


accent('Hero face moon accent', (-1.6, 0, .87), 15, .48)
accent('Musician face moon accent', (1.6, 0, .74), 15, .48)
for index, x in enumerate((-2.35, -.92, .88, 2.36)):
    accent('Hand moon accent %02d' % index, (x, 0, .55), 4, .40)

nodes, links = floor.node_tree.nodes, floor.node_tree.links
shader = nodes.get('Principled BSDF')
base = shader.inputs['Base Color'].links[0].from_socket
normal = shader.inputs['Normal'].links[0].from_socket
position = nodes.new('ShaderNodeNewGeometry')
noise = nodes.new('ShaderNodeTexNoise')
noise.name = 'Irregular damp patches'
noise.inputs['Scale'].default_value = .65
noise.inputs['Detail'].default_value = 3
noise.inputs['Roughness'].default_value = .72
links.new(position.outputs['Position'], noise.inputs['Vector'])
roughness = nodes.new('ShaderNodeValToRGB')
roughness.name = 'Dry stone to localized wet patches'
roughness.color_ramp.elements[0].position = .38
roughness.color_ramp.elements[0].color = (.78, .78, .78, 1)
roughness.color_ramp.elements[1].position = .68
roughness.color_ramp.elements[1].color = (.18, .18, .18, 1)
links.new(noise.outputs['Fac'], roughness.inputs[0])
links.new(roughness.outputs['Color'], shader.inputs['Roughness'])
tint = nodes.new('ShaderNodeValToRGB')
tint.name = 'Damp stone darkening'
tint.color_ramp.elements[0].position = .38
tint.color_ramp.elements[0].color = (1, 1, 1, 1)
tint.color_ramp.elements[1].position = .68
tint.color_ramp.elements[1].color = (.58, .58, .58, 1)
links.new(noise.outputs['Fac'], tint.inputs[0])
mix = nodes.new('ShaderNodeMixRGB')
mix.blend_type = 'MULTIPLY'
mix.inputs[0].default_value = 1
links.new(base, mix.inputs[1])
links.new(tint.outputs['Color'], mix.inputs[2])
links.new(mix.outputs[0], shader.inputs['Base Color'])
grain = nodes.new('ShaderNodeTexNoise')
grain.name = 'Fine stone grain'
grain.inputs['Scale'].default_value = 85
links.new(position.outputs['Position'], grain.inputs['Vector'])
bump = nodes.new('ShaderNodeBump')
bump.inputs['Strength'].default_value = .22
bump.inputs['Distance'].default_value = .008
links.new(grain.outputs['Fac'], bump.inputs['Height'])
links.new(normal, bump.inputs['Normal'])
links.new(bump.outputs['Normal'], shader.inputs['Normal'])

unchanged = all(obj.name in geometry and len(obj.data.vertices) == geometry[obj.name][0]
                and obj.matrix_world == geometry[obj.name][1]
                for obj in scene.objects if obj.type == 'MESH')
if not unchanged or scene.camera.matrix_world != camera_matrix:
    raise RuntimeError('Geometry or camera changed unexpectedly.')
scene.render.filepath = str(folder / 'blender-night-refined.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-night-refined.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'NightRefinement.json').write_text(json.dumps({
    'geometryUnchanged': unchanged, 'cameraUnchanged': True,
    'exposure': scene.view_settings.exposure,
    'worldStrength': scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value,
    'addedLights': 6, 'faceAccentWatts': 15, 'handAccentWatts': 4,
    'floorRoughnessRange': [.18, .78],
    'scope': 'Blender-only character light accents and stone material refinement. Unity unchanged.',
}, indent=2))
print('BLENDER_NIGHT_REFINEMENT_PASS')
