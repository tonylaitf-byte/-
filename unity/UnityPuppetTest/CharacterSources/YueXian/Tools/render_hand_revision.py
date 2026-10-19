"""Render the saved, final source pose without rebuilding its meshes."""
import bpy
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'Source/YueXian_and_Yueqin.blend'))
s=bpy.context.scene;cam=s.camera;s.cycles.samples=24
def view(name,position,target,scale,width=1200,height=1200):
    cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    s.render.resolution_x=width;s.render.resolution_y=height;s.render.filepath=str(root/'Previews'/f'{name}.png');bpy.ops.render.render(write_still=True)
view('YueXian',(2,-6,2.7),(0,-.18,.96),2.32,1200,1400)
view('YueXian_Detail',(.8,-4,2.01),(0,-.24,1.43),1.10)
rig=bpy.data.objects['YueXianRig']
for stem in ['Left','Right']:
    hand=rig.pose.bones[stem+'Hand'];target=hand.head+(hand.tail-hand.head)*.8
    view(f'Hand_{stem}_Holding',target+Vector((-.06,-1,.12)),target,.40,1000,1000)
