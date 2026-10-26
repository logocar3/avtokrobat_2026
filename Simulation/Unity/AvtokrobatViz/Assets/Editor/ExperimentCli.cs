// Runs experiment files on the Unity engine without pressing Play:
//   - headless:  Unity.exe -batchmode -nographics -projectPath <project> -executeMethod
//                Avtokrobat.ExperimentCli.Run -experiments <file|folder> ...   (run-unity-experiment.ps1)
//   - in the editor: menu Avtokrobat > Run Experiments...  (pick a file; runs to the end, blocking)
// Results go to Simulation/output/unity_experiments/<name>/. Lines starting with
// "[experiment]" are the summary the PowerShell script prints.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avtokrobat
{
    public static class ExperimentCli
    {
        public static void Run()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            var files = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-experiments") continue;
                for (int k = i + 1; k < args.Length && !args[k].StartsWith("-"); k++) files.AddRange(Expand(args[k]));
            }
            if (files.Count == 0)
            {
                Debug.Log("[experiment] nothing to run: pass -experiments <file.json | folder> ...");
                EditorApplication.Exit(2);
                return;
            }
            int failed = RunAll(files);
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        [MenuItem("Avtokrobat/Run Experiment File...")]
        static void RunFromMenu()
        {
            string dir = Path.Combine(SimConfig.SimulationDir, "experiments");
            string f = EditorUtility.OpenFilePanel("Run experiment (Unity engine)", dir, "json");
            if (string.IsNullOrEmpty(f)) return;
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Avtokrobat", "Stop Play mode first, or use the Experiments panel in the Unity PhysX mode.", "OK");
                return;
            }
            RunAll(new List<string> { f });
            EditorUtility.RevealInFinder(ExperimentRunner.OutputRoot);
        }

        [MenuItem("Avtokrobat/Run All Experiments")]
        static void RunAllFromMenu()
        {
            if (EditorApplication.isPlaying) return;
            RunAll(ExperimentRunner.Available());
            EditorUtility.RevealInFinder(ExperimentRunner.OutputRoot);
        }

        static IEnumerable<string> Expand(string a)
        {
            string p = Path.IsPathRooted(a) ? a : Path.Combine(SimConfig.SimulationDir, a);
            if (Directory.Exists(p))
            {
                var l = new List<string>(Directory.GetFiles(p, "*.json"));
                l.Sort();
                return l;
            }
            return new[] { p };
        }

        static int RunAll(List<string> files)
        {
            SimulationMode prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            int failed = 0;
            var host = new GameObject("ExperimentHost").transform;
            try
            {
                foreach (string f in files)
                {
                    var r = new ExperimentRunner(f);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        string msg = r.RunToEnd(host);
                        Debug.Log("[experiment] " + msg + string.Format("  ({0:0.0} s wall)", sw.Elapsed.TotalSeconds));
                    }
                    catch (System.Exception e)
                    {
                        failed++;
                        Debug.LogError("[experiment] FAILED " + Path.GetFileName(f) + ": " + e.Message);
                    }
                    finally { r.Destroy(); }
                }
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
                Physics.simulationMode = prev;
            }
            Debug.Log("[experiment] " + (files.Count - failed) + " of " + files.Count + " experiments ok");
            return failed;
        }
    }
}
