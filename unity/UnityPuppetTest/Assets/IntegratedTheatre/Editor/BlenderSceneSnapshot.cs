using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PalmTheatre.Integration;

public static class BlenderSceneSnapshot
{
    const string Pending = "BlenderSceneSnapshot.Pending";
    const string DisplayPose = "BlenderSceneSnapshot.DisplayPose";
    const string Folder = "../../reports/blender-render";
    [Serializable] sealed class Surface {
        public string name, texture, normal, shader;
        public Color color;
        public float metallic, roughness;
        public Vector2 scale, offset;
    }
    [Serializable] sealed class Indices { public int[] values; }
    [Serializable] sealed class Part {
        public string name;
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector2[] uv;
        public Indices[] triangles;
        public int[] materials;
    }
    [Serializable] sealed class Snapshot {
        public List<Surface> materials = new List<Surface>();
        public List<Part> parts = new List<Part>();
        public Vector3 cameraPosition, cameraForward, cameraUp;
        public float fieldOfView;
        public string[] actorObjectNames;
    }
    static int frames;
    public static void Capture() {
        SessionState.SetBool(DisplayPose, false); Begin();
    }
    public static void CaptureDisplayPose() {
        SessionState.SetBool(DisplayPose, true); Begin();
    }
    static void Begin() {
        Directory.CreateDirectory(Folder);
        EditorSceneManager.OpenScene(HybridTheatreBuilder.DuetScene);
        SessionState.SetBool(Pending, true);
        EditorPlayModeStartup.EnterAfterInitialization();
    }
    [InitializeOnLoadMethod] static void Resume() {
        if (!SessionState.GetBool(Pending, false)) return;
        frames = 0; EditorApplication.update += Tick;
    }
    static void Tick() {
        if (!EditorApplication.isPlaying) return;
        try {
            if (++frames < 10) return;
            var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
            if (!session.Initialize()) throw new Exception("Scene initialization failed.");
            session.automaticUpdate = false; session.enableAudio = false; AudioListener.volume = 0;
            foreach (var actor in new[] { session.leftActor, session.rightActor }) {
                actor.SetInstrumentVisible(true);
                actor.Accept(11, true, new PuppetControlState {version=2,controlMode="hybrid",inputEpoch=1,
                    handDetected=true,calibrated=true,sequenceId=1}, out _, out _);
            }
            for (int i=0; i<180; i++) session.Tick(1f/60);
            if (SessionState.GetBool(DisplayPose, false)) {
                foreach (var actor in new[] {session.leftActor, session.rightActor}) {
                    actor.Accept(11, true, new PuppetControlState {version=2,controlMode="hybrid",inputEpoch=1,
                        handDetected=true,calibrated=true,sequenceId=2,actionSequence=1,
                        action=actor==session.leftActor ? "ALERT" : "LANDSCAPE",
                        leftHandY=-.65f,rightHandY=-.65f}, out _, out _);
                    if (!actor.TryStart(0,1)) throw new Exception("Display action failed: " + actor.role);
                }
                for (int i=0; i<90; i++) session.Tick(1f/60);
                File.WriteAllText(Folder+"/display-rig-inspection.txt",string.Join("\n",new[] {session.leftActor,session.rightActor}
                    .Select(a=>a.role+": "+a.ActiveAction.actionId+"; bones: "+string.Join(", ",a.bones.Select(b=>b.path)))));
            }
            Export();
            Finish(0);
        } catch (Exception error) { File.WriteAllText(Folder+"/export-error.txt",error.ToString()); Finish(1); }
    }
    static void Finish(int code) {
        EditorApplication.update -= Tick; SessionState.SetBool(Pending,false); EditorApplication.Exit(code);
    }
    static string TexturePath(Texture texture) {
        string path = texture ? AssetDatabase.GetAssetPath(texture) : "";
        return File.Exists(path) ? Path.GetFullPath(path) : "";
    }
    static void Export() {
        var result = new Snapshot(); var ids = new Dictionary<Material,int>();
        var session = UnityEngine.Object.FindAnyObjectByType<HybridTheatreSession>();
        bool display = SessionState.GetBool(DisplayPose,false);
        result.actorObjectNames = new[] {session.leftActor,session.rightActor}.SelectMany(a=>a.GetComponentsInChildren<Renderer>(true)).Select(r=>r.name).Distinct().ToArray();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>()) {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (display && !renderer.transform.IsChildOf(session.leftActor.transform) &&
                !renderer.transform.IsChildOf(session.rightActor.transform)) continue;
            Mesh mesh; bool baked = renderer is SkinnedMeshRenderer;
            if (baked) { mesh = new Mesh(); ((SkinnedMeshRenderer)renderer).BakeMesh(mesh, true); }
            else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh) continue;
            var slots = renderer.sharedMaterials;
            var materials = new List<int>();
            foreach (var material in slots) {
                if (!ids.TryGetValue(material, out int id)) {
                    id = result.materials.Count; ids.Add(material,id);
                    result.materials.Add(new Surface { name=material.name, shader=material.shader.name,
                        color=SurfaceColor(material),
                        metallic=material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0,
                        roughness=1-(material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") :
                            material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : .3f),
                        texture=TexturePath(material.mainTexture),
                        normal=TexturePath(material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null),
                        scale=material.mainTextureScale, offset=material.mainTextureOffset });
                }
                materials.Add(id);
            }
            result.parts.Add(new Part { name=renderer.name,
                vertices=mesh.vertices.Select(renderer.transform.TransformPoint).ToArray(), uv=mesh.uv,
                normals=mesh.normals.Select(n=>renderer.localToWorldMatrix.inverse.transpose.MultiplyVector(n).normalized).ToArray(),
                triangles=Enumerable.Range(0,mesh.subMeshCount).Select(i=>new Indices {values=mesh.GetTriangles(i)}).ToArray(), materials=materials.ToArray() });
            if (baked) UnityEngine.Object.DestroyImmediate(mesh);
        }
        var camera = Camera.main;
        result.cameraPosition=camera.transform.position; result.cameraForward=camera.transform.forward;
        result.cameraUp=camera.transform.up; result.fieldOfView=camera.fieldOfView;
        File.WriteAllText(Folder+(display ? "/display-pose-snapshot.json" : "/scene-snapshot.json"),JsonUtility.ToJson(result));
        Debug.Log("BLENDER_SCENE_SNAPSHOT_PASS " + result.parts.Count);
    }
    static Color SurfaceColor(Material material) {
        foreach (string key in new[] { "_BaseColor", "_StoneColor", "_Color" })
            if (material.HasProperty(key)) return material.GetColor(key).linear;
        return Color.white;
    }
}
