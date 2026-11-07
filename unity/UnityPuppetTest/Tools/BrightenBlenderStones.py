"""A small exposure adjustment to the approved low-key uneven-stone trial."""
import bpy
import json
import sys
from pathlib import Path

folder = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
scene = bpy.context.scene
before = scene.view_settings.exposure
scene.view_settings.exposure = before + .35
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if any(d.use for d in prefs.devices) else 'CPU'
except Exception:
    scene.cycles.device = 'CPU'
scene.render.filepath = str(folder / 'blender-uneven-stones-brighter.png')
bpy.ops.wm.save_as_mainfile(filepath=str(folder / 'courtyard-uneven-stones-brighter.blend'))
bpy.ops.render.render(write_still=True)
(folder / 'StonesBrightness.json').write_text(json.dumps({
    'exposureBefore': before,
    'exposureAfter': scene.view_settings.exposure,
    'changeStops': .35,
    'scope': 'Exposure only; camera, geometry, materials and light settings unchanged. Blender-only trial.',
}, indent=2))
print('BLENDER_STONES_BRIGHTNESS_PASS')
