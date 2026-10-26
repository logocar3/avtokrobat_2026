// Loads the two config files the Unity engine runs on, from the Simulation folder (three
// levels above Assets/):
//   config/scene.json        driver schedule, controller gains, grip, sensors (JsonUtility subset)
// The car files (cars/*.json, *.car) are loaded by CarLibrary.cs.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Avtokrobat
{
    // ---- the subset of config/scene.json this engine uses (JsonUtility, names must match) ----
    [System.Serializable]
    public class PhysConfigFile
    {
        public PhysSimCfg sim = new PhysSimCfg();
        public PhysVehicleCfg vehicle = new PhysVehicleCfg();
        public PhysSceneCfg scene = new PhysSceneCfg();
        public PhysDriverCfg driver = new PhysDriverCfg();
        public PhysControllerCfg controller = new PhysControllerCfg();
        public SensorsCfg sensors = new SensorsCfg();
    }

    [System.Serializable] public class PhysSimCfg { public float duration = 8f; }
    [System.Serializable] public class PhysTireCfg { public float mu = 1.1f; }
    [System.Serializable] public class PhysVehicleCfg { public float[] init_xyz = { 0f, 0f, 0.3f }; public float init_yaw_deg; public PhysTireCfg tire = new PhysTireCfg(); }
    [System.Serializable] public class PhysSceneCfg { public float friction = 1.1f; }

    [System.Serializable]
    public class PhysDriverCfg
    {
        public float settle_time = 0.5f, throttle_off_time = 2.8f, brake_time = 3.4f, brake_level = 0.6f;
    }

    [System.Serializable]
    public class PhysControllerCfg
    {
        public float speed_kp = 0.00025f, speed_ki = 0.0015f, speed_window = 0.02f;
    }

    public static class SimConfig
    {
        public static string SimulationDir
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        static string ConfigPath(string file) { return Path.Combine(SimulationDir, "config", file); }

        // scene.json + the driver's steering schedule (JsonUtility can't read [[t, cmd], ...])
        public static PhysConfigFile LoadScene(List<Vector2> steerSchedule, out string source)
        {
            string path = ConfigPath("scene.json");
            if (!File.Exists(path))
            {
                source = "built-in defaults (config/scene.json not found)";
                if (steerSchedule != null) steerSchedule.Add(Vector2.zero);
                return new PhysConfigFile();
            }
            string text = File.ReadAllText(path);
            var cfg = JsonUtility.FromJson<PhysConfigFile>(text);
            if (steerSchedule != null)
            {
                Match list = Regex.Match(text, "\"steering\"\\s*:\\s*\\[(.*?)\\]\\s*\\]", RegexOptions.Singleline);
                if (list.Success)
                    foreach (Match p in Regex.Matches(list.Groups[1].Value + "]", "\\[\\s*([-+0-9.eE]+)\\s*,\\s*([-+0-9.eE]+)\\s*\\]"))
                        steerSchedule.Add(new Vector2(float.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture),
                                                      float.Parse(p.Groups[2].Value, CultureInfo.InvariantCulture)));
                steerSchedule.Sort((a, b) => a.x.CompareTo(b.x));
            }
            source = path;
            return cfg;
        }
    }
}
