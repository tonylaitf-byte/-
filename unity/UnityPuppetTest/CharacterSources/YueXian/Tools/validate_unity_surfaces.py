"""Check the actual imported Unity skinning, exported by Play mode validation."""
import json,sys
from pathlib import Path
from mathutils.bvhtree import BVHTree
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
root=Path(args[0]) if args else Path(__file__).resolve().parents[1]
source=root/'YueXianReports/UnitySurfaceSamples.json'
data=json.loads(source.read_text())
def tree(surface):
    vertices=[(v['x'],v['y'],v['z']) for v in surface['vertices']]
    t=surface['triangles'];return BVHTree.FromPolygons(vertices,[t[i:i+3] for i in range(0,len(t),3)],all_triangles=True)
report={'source':'Unity BakeMesh, Bone4; default holding pose and both extrema of the built-in plucking demonstration','poses':[]}
for pose in data['poses']:
    sleeves=tree(pose['sleeves']);hands=tree(pose['hands']);prop=tree(pose['instrument'])
    report['poses'].append({'pluck_degrees':pose['pluckDegrees'],'sleeve_instrument_intersections':len(sleeves.overlap(prop)),'sleeve_hand_intersections':len(sleeves.overlap(hands)),'sleeve_triangles':len(pose['sleeves']['triangles'])//3})
report['passed']=all(p['sleeve_instrument_intersections']==0 and p['sleeve_hand_intersections']==0 for p in report['poses'])
(root/'YueXianReports/UnitySurfaceValidation.json').write_text(json.dumps(report,indent=2))
print('UNITY_SURFACE_VALIDATION',json.dumps(report),flush=True)
if not report['passed']:raise RuntimeError('Unity sleeve intersections remain; see UnitySurfaceValidation.json')
