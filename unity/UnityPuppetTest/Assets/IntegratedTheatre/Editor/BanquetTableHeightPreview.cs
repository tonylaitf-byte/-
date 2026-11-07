using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BanquetTableHeightPreview
{
    const string Folder = "../../reports/table-height-double";
    const float Raise = .4375f;
    [Serializable] sealed class Item { public string name; public Vector3 center, size; }
    [Serializable] sealed class Snapshot { public Item[] items; }
    [Serializable] sealed class Result
    {
        public bool passed, tabletopItemsMovedTogether, floorPropsUnchanged, tableFeetGrounded;
        public float raise = Raise, beforeTopHeight, afterTopHeight;
    }
    public static void Before()
    {
        var snapshot = Measure();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/Before.json", JsonUtility.ToJson(snapshot, true));
        CourtyardLayoutPreview.RenderScene(HeroRefinementPreview.Scene, Folder + "/before");
    }
    public static void After()
    {
        var snapshot = Measure();
        var baseline = JsonUtility.FromJson<Snapshot>(File.ReadAllText(Folder + "/Before.json"));
        var result = new Result { tabletopItemsMovedTogether = true, floorPropsUnchanged = true };
        var oldTable = baseline.items.Single(i => i.name == "Low Table Mesh");
        var table = snapshot.items.Single(i => i.name == "Low Table Mesh");
        float floor = GameObject.Find("wet stone courtyard").GetComponent<Renderer>().bounds.max.y;
        result.beforeTopHeight = oldTable.center.y + oldTable.size.y * .5f - floor;
        result.afterTopHeight = table.center.y + table.size.y * .5f - floor;
        result.tableFeetGrounded = Mathf.Abs((table.center.y - table.size.y * .5f) -
            (oldTable.center.y - oldTable.size.y * .5f)) < .001f;
        foreach (var item in snapshot.items.Where(i => i.name != "Low Table Mesh"))
        {
            var old = baseline.items.Single(i => i.name == item.name);
            bool tabletop = item.name.StartsWith("Wine Cup") || item.name == "Wine Ewer Mesh" ||
                item.name == "Table Wine Jar Mesh" || item.name.EndsWith("Dish") || item.name.EndsWith("Tabletop Details");
            float movement = tabletop ? Raise : 0;
            bool valid = Vector3.Distance(item.center, old.center + Vector3.up * movement) < .001f &&
                Vector3.Distance(item.size, old.size) < .001f;
            if (tabletop) result.tabletopItemsMovedTogether &= valid;
            else result.floorPropsUnchanged &= valid;
        }
        result.passed = result.tabletopItemsMovedTogether && result.floorPropsUnchanged && result.tableFeetGrounded &&
            Mathf.Abs(result.afterTopHeight - result.beforeTopHeight - Raise) < .001f &&
            Mathf.Abs(result.afterTopHeight - result.beforeTopHeight * 2) < .001f && snapshot.items.Length == baseline.items.Length;
        File.WriteAllText(Folder + "/After.json", JsonUtility.ToJson(snapshot, true));
        File.WriteAllText(Folder + "/Validation.json", JsonUtility.ToJson(result, true));
        if (!result.passed) throw new InvalidOperationException("Table height checks failed; see " + Folder + "/Validation.json");
        CourtyardLayoutPreview.RenderScene(HeroRefinementPreview.Scene, Folder + "/after");
    }
    static Snapshot Measure()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene(HeroRefinementPreview.Scene);
        var root = GameObject.Find("Courtyard Banquet Blender Assets");
        return new Snapshot { items = root.GetComponentsInChildren<Renderer>().Select(r =>
            new Item { name = r.name, center = r.bounds.center, size = r.bounds.size }).ToArray() };
    }
}
