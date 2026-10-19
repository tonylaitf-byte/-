using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using PalmTheatre.MoonlitDuet;

// Export the actual Unity deformation, including import axes and Bone4 skinning.
// The accompanying Blender QA tool checks these triangle surfaces with a BVH.
public static class YueXianSurfaceValidation
{
    [Serializable] public class Surface
    {
        public Vector3[] vertices;
        public int[] triangles;
    }
    [Serializable] public class Sample
    {
        public float pluckDegrees;
        public Surface sleeves, hands, instrument;
    }
    [Serializable] public class Samples { public Sample[] poses; }
    sealed class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        public void Triangle(Vector3 a, Vector3 b, Vector3 c) { vertices.Add(a); vertices.Add(b); vertices.Add(c); }
        public Surface Finish() => new Surface { vertices = vertices.ToArray(), triangles = Enumerable.Range(0, vertices.Count).ToArray() };
    }
    static bool Hand(string n) => n.EndsWith("Hand") || new[] { "Index", "Middle", "Ring", "Little", "Thumb" }.Any(n.Contains);
    static bool Sleeve(string n) => n.EndsWith("UpperArm") || n.EndsWith("Forearm") || Hand(n);
    static bool Weighted(BoneWeight w, Transform[] bones, Func<string, bool> predicate)
    {
        return (w.weight0 > .3f && predicate(bones[w.boneIndex0].name)) ||
            (w.weight1 > .3f && predicate(bones[w.boneIndex1].name)) ||
            (w.weight2 > .3f && predicate(bones[w.boneIndex2].name)) ||
            (w.weight3 > .3f && predicate(bones[w.boneIndex3].name));
    }
    public static void Capture(YueXianPerformance player)
    {
        bool enabled = player.enabled;
        player.enabled = false;
        Quaternion hand = player.rightHand.localRotation, index = player.rightIndex.localRotation, chest = player.chest.localRotation;
        var samples = new List<Sample>();
        try
        {
            foreach (float phase in new[] { -1f, 0f, 1f })
            {
                player.rightHand.localRotation = hand * Quaternion.Euler(0, 0, phase * 4.5f);
                player.rightIndex.localRotation = index * Quaternion.Euler(phase * 7, 0, 0);
                player.chest.localRotation = chest * Quaternion.Euler(phase * .35f, 0, 0);
                var sleeves = new Builder(); var hands = new Builder(); var instrument = new Builder();
                foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    try
                    {
                        renderer.BakeMesh(mesh);
                        var positions = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
                        var weights = renderer.sharedMesh.boneWeights;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            bool skin = renderer.sharedMaterials[sub].name.Contains("Porcelain");
                            int[] triangles = mesh.GetTriangles(sub);
                            for (int i = 0; i < triangles.Length; i += 3)
                            {
                                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                                Func<string, bool> predicate = skin ? (Func<string, bool>)Hand : Sleeve;
                                bool selected = Weighted(weights[a], renderer.bones, predicate) || Weighted(weights[b], renderer.bones, predicate) || Weighted(weights[c], renderer.bones, predicate);
                                if (selected) (skin ? hands : sleeves).Triangle(positions[a], positions[b], positions[c]);
                            }
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                var prop = player.GetComponentsInChildren<Transform>().First(t => t.name == "Yueqin_Independent");
                foreach (var filter in prop.GetComponentsInChildren<MeshFilter>())
                {
                    var points = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                    var triangles = filter.sharedMesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        instrument.Triangle(points[triangles[i]], points[triangles[i + 1]], points[triangles[i + 2]]);
                }
                samples.Add(new Sample { pluckDegrees = phase * 4.5f, sleeves = sleeves.Finish(), hands = hands.Finish(), instrument = instrument.Finish() });
            }
            File.WriteAllText("YueXianReports/UnitySurfaceSamples.json", JsonUtility.ToJson(new Samples { poses = samples.ToArray() }));
        }
        finally
        {
            player.rightHand.localRotation = hand; player.rightIndex.localRotation = index; player.chest.localRotation = chest;
            player.enabled = enabled;
        }
    }
}
