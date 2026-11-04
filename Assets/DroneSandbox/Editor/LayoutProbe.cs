using System.IO;
using UnityEngine;

namespace DroneSandbox.EditorTools
{
    // Draws why each square metre of a sector's valley floor is or isn't farmed, to tune the field planner without
    // a full build. Command line:
    //   Unity.exe -batchmode -quit -projectPath . -executeMethod DroneSandbox.EditorTools.LayoutProbe.Run -probeOut <png>
    public static class LayoutProbe
    {
        public static void Run()
        {
            string output = Path.Combine(Path.GetTempPath(), "layout_probe.png");
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-probeOut") output = args[i + 1];

            var clock = System.Diagnostics.Stopwatch.StartNew();
            MapLayout.Prepare();
            long prepareMs = clock.ElapsedMilliseconds;
            const int half = 160;
            var tex = new Texture2D(2 * half, 2 * half, TextureFormat.RGB24, false);
            var counts = new int[10];
            string[] names = { "field", "field margin", "road", "river", "lake", "dam/works", "site", "village", "open", "edge" };
            Color[] colours =
            {
                new Color(0.85f, 0.7f, 0.3f), new Color(0.95f, 0.9f, 0.6f), new Color(0.35f, 0.35f, 0.35f), new Color(0.2f, 0.3f, 0.9f),
                new Color(0.3f, 0.8f, 0.9f), new Color(0.6f, 0.3f, 0.8f), new Color(0.9f, 0.2f, 0.2f), new Color(1f, 0.5f, 0.1f),
                new Color(0.25f, 0.6f, 0.25f), Color.black,
            };
            for (int j = 0; j < 2 * half; j++)
            {
                for (int i = 0; i < 2 * half; i++)
                {
                    var p = new Vector2(i - half + 0.5f, j - half + 0.5f);
                    int why;
                    if (MapLayout.InField(p, 0f)) why = 0;
                    else if (MapLayout.InField(p, 3f)) why = 1;
                    else if (MapLayout.RoadField.Distance(p.x, p.y) < MapLayout.RoadHalfWidth + 2f) why = 2;
                    else if (MapLayout.HasRiver && MapLayout.RiverField.Distance(p.x, p.y) < MapLayout.RiverHalfWidth + 6f) why = 3;
                    else if (MapLayout.LakeDistance(p.x, p.y) < 16f) why = 4;
                    else if (MapLayout.NearDam(p, 15f) || MapLayout.OnWorks(p, 4f)) why = 5;
                    else if (MapLayout.NearFlatSite(p, 4f)) why = 6;
                    else if (MapLayout.NearVillage(p, 0f)) why = 7;
                    else if (Mathf.Abs(p.x) > 210f || Mathf.Abs(p.y) > 210f) why = 9;
                    else why = 8;
                    counts[why]++;
                    tex.SetPixel(i, j, colours[why]);
                }
            }
            File.WriteAllBytes(output, tex.EncodeToPNG());
            var report = new System.Text.StringBuilder($"Layout probe: prepared in {prepareMs} ms, {MapLayout.Fields.Count} fields, coverage {MapLayout.FieldCoverage():P0};");
            for (int k = 0; k < counts.Length; k++)
                if (counts[k] > 0) report.Append($" {names[k]} {counts[k] * 100f / (4f * half * half):0}%");
            Debug.Log(report.ToString());
        }
    }
}
