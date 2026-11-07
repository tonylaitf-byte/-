"""Create a low-key moonlit lighting variant of the saved Cycles trial."""
import bpy
import json
import sys
from pathlib import Path

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
scene = bpy.context.scene
# Compute-device preferences are not stored inside a .blend file.
try:
    preferences = bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type = 'OPTIX'
    preferences.get_devices()
    for device in preferences.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(d.use for d in preferences.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'
energies = {
    'Cool moon softbox': 450,
    'Soft face fill': 65,
    'Warm courtyard bounce': 85,
    'Temple rim': 350,
}
for light in bpy.data.lights:
    if light.name in energies:
        light.energy = energies[light.name]
    elif light.name.startswith('Warm floor lantern'):
        light.energy = 14
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .035
for material in bpy.data.materials:
    if not material.use_nodes:
        continue
    for node in material.node_tree.nodes:
        if material.name == 'BanquetBackdrop' and node.type == 'EMISSION':
            node.inputs['Strength'].default_value = .28
        if node.type == 'PRINCIPLED_VOLUME':
            node.inputs['Density'].default_value = .0025
scene.view_settings.exposure = -.65
scene.render.filepath = str(folder / 'blender-moonlit-night.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-moonlit-night.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'NightRender.json').write_text(json.dumps({
    'engine': scene.render.engine,
    'resolution': [scene.render.resolution_x, scene.render.resolution_y],
    'exposure': scene.view_settings.exposure,
    'worldStrength': .035,
    'lightEnergyWatts': energies,
    'scope': 'Lighting-only variant; same camera, geometry and materials as the bright Cycles trial. Unity unchanged.',
}, indent=2))
print('BLENDER_NIGHT_RENDER_PASS')
