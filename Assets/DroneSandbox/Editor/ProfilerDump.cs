using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DroneSandbox.EditorTools
{
    // Reads the profiler captures the autotest records round its flight hitches (profile_*.raw, from a development
    // build) and writes, for every frame over the threshold, what each thread spent it on: the samples over 2 ms, a few
    // levels deep. Command line: -executeMethod DroneSandbox.EditorTools.ProfilerDump.Run -dumpFolder <folder>
    public static class ProfilerDump
    {
        const float SlowFrameMs = 40f, SampleMs = 0.25f;
        const int Depth = 12;

        // Every capture in the folder, into hitches.txt beside them.
        public static void Run()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-dumpFolder");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
            var text = new StringBuilder();
            foreach (var file in Directory.GetFiles(folder, "profile_*.raw"))
            {
                text.AppendLine($"=== {Path.GetFileName(file)}");
                if (!ProfilerDriver.LoadProfile(file, false)) { text.AppendLine("  could not load"); continue; }
                for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
                {
                    float frameMs;
                    using (var main = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                               HierarchyFrameDataView.columnTotalTime, false))
                    {
                        if (main == null || !main.valid) continue;
                        frameMs = main.frameTimeMs;
                        if (frameMs < SlowFrameMs) continue;
                        text.AppendLine($"frame {frame}: {frameMs:F1} ms");
                        Write(main, text);
                    }
                    // The other threads that worked in that frame: the render thread and the job workers.
                    for (int thread = 1; thread < 64; thread++)
                    {
                        using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                                   HierarchyFrameDataView.columnTotalTime, false))
                        {
                            if (view == null || !view.valid) break;
                            if (view.threadName.Contains("Render") || view.threadName.Contains("Gfx")) Write(view, text);
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(folder, "hitches.txt"), text.ToString());
            Debug.Log($"Profiler dump written to {Path.Combine(folder, "hitches.txt")}");
        }

        // A thread's samples over SampleMs, indented by depth.
        static void Write(HierarchyFrameDataView view, StringBuilder text)
        {
            text.AppendLine($"  [{view.threadGroupName} {view.threadName}]");
            var children = new List<int>();
            void Walk(int id, int depth)
            {
                if (depth > Depth) return;
                view.GetItemChildren(id, children);
                var mine = new List<int>(children);
                foreach (int child in mine)
                {
                    float ms = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                    if (ms < SampleMs) continue;
                    text.AppendLine($"  {new string(' ', depth * 2)}{view.GetItemName(child)} {ms:F1} ms");
                    Walk(child, depth + 1);
                }
            }
            Walk(view.GetRootItemID(), 1);
        }
    }
}
