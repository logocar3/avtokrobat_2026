// Runs one experiment file (experiments/*.json, format in experiments/README.md) on the Unity
// engine: the UDIRC 1610S + VESC + 1 kHz controller in a JSON-built environment. Writes
// internal.csv, external.csv, truth.csv and experiment.json (as run, defaults filled in) to
// output/unity_experiments/<name>_<yyyyMMdd-HHmmss>/ (a new folder for every run).
//
// Use it stepwise (Begin, then Step() until it returns false, then Finish) to watch a run, or
// RunToEnd() headless (ExperimentCli / run-unity-experiment.ps1).
//
// Fields beyond the original experiment format (src/experiment.py):
//   "scene":       "flat" | "jump" | an environment file (experiments/environments/<name>.json)
//   "environment": an inline environment {"pieces": [...]} (wins over "scene")
//   "car":         a car from cars/ (default "udirc_1610s"), a student's "my_car", or a mystery
//                  car "mystery_A.car" (encrypted: no truth.csv, no overrides, see CarLibrary.cs)
//   "car_overrides": any subset of the car file, e.g. {"unity_model": {"mass_kg": 1.6}}
//   "tire_mu":     tyre grip (default min(car unity_model.tire.mu, scene.json scene.friction))
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using static Avtokrobat.MiniJson;

namespace Avtokrobat
{
    public class ExperimentRunner
    {
        public static Dictionary<string, object> Defaults()
        {
            return (Dictionary<string, object>)Parse(@"{
              ""description"": """", ""car"": ""udirc_1610s"", ""scene"": ""flat"", ""duration"": 5.0, ""seed"": 0,
              ""initial_state"": {""position"": [0.0, 0.0, null], ""yaw_deg"": 0.0, ""pitch_deg"": 0.0,
                                  ""roll_deg"": 0.0, ""speed"": 0.0, ""velocity"": [0.0, 0.0, 0.0],
                                  ""angular_velocity_deg_s"": [0.0, 0.0, 0.0]},
              ""inputs"": {""motor_mode"": ""torque"", ""motor"": 0.0, ""steering"": 0.0},
              ""sensors"": {""noise"": true},
              ""outputs"": {""truth"": true, ""rate_hz"": 200.0, ""truth_rate_hz"": 50.0},
              ""stop_if_off_world"": true
            }");
        }

        public string Name { get; private set; }
        public string OutDir { get; private set; }
        public string Message { get; private set; }
        public string Description { get; private set; }
        public double Duration { get; private set; }
        public UdircSim Sim { get; private set; }
        public EnvironmentInfo Env { get; private set; }
        public bool Done { get; private set; }
        public bool Mystery { get; private set; }             // car parameters hidden, no truth.csv
        public CarInfo Car { get; private set; }

        readonly string path;
        Dictionary<string, object> exp;
        Transform root;
        Func<double, double> motorSig, steerSig;
        MotorMode mode;
        int sigEvery, truthEvery, n;
        bool writeTruth, stopOffWorld;
        StringBuilder internalCsv, externalCsv, truthCsv;
        string stopReason = "";
        DateTime started;

        public ExperimentRunner(string experimentPath)
        {
            path = Path.GetFullPath(experimentPath);
        }

        public static string OutputRoot
        {
            get { return Path.Combine(SimConfig.SimulationDir, "output", "unity_experiments"); }
        }

        // Build the world + car and get ready to step. Throws with a readable message on a bad file.
        public void Begin(Transform parent)
        {
            var user = Parse(File.ReadAllText(path)) as Dictionary<string, object>;
            if (user == null) throw new FormatException(Path.GetFileName(path) + ": the file must contain one JSON object");
            if (!Has(user, "inputs")) throw new FormatException(Path.GetFileName(path) + ": \"inputs\" is required");
            exp = Merge(Defaults(), user);
            Name = JStr(exp, "name", Path.GetFileNameWithoutExtension(path));
            Description = JStr(exp, "description", "");
            Duration = JNum(exp, "duration", 5.0);
            started = DateTime.Now;
            exp["name"] = Name;
            exp["engine"] = "unity-physx";
            exp["run_started"] = started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            var inputs = JObj(exp, "inputs");
            string mm = JStr(inputs, "motor_mode", "torque");
            if (mm != "torque" && mm != "speed") throw new FormatException("inputs.motor_mode must be \"torque\" or \"speed\"");
            mode = mm == "speed" ? MotorMode.Speed : MotorMode.Torque;
            motorSig = Signals.Make(inputs.ContainsKey("motor") ? inputs["motor"] : 0.0, "inputs.motor");
            steerSig = Signals.Make(inputs.ContainsKey("steering") ? inputs["steering"] : 0.0, "inputs.steering");

            string src;
            PhysConfigFile cfg = SimConfig.LoadScene(null, out src);
            CarInfo carInfo;
            UdircFile car = CarLibrary.Load(JStr(exp, "car", CarLibrary.Reference), JObj(exp, "car_overrides"),
                                            Path.GetDirectoryName(path), out carInfo);
            Car = carInfo;
            Mystery = carInfo.mystery;
            if (Mystery && Has(user, "tire_mu"))
                throw new FormatException("a mystery car can't be changed: remove \"tire_mu\"");
            exp["car"] = carInfo.name;
            if (Mystery) exp["car_mystery"] = true;
            float sceneMu = cfg.scene.friction;
            float tireMu = (float)JNum(exp, "tire_mu", Mathf.Min(car.unity_model.tire.mu, sceneMu));

            root = new GameObject("Experiment " + Name).transform;
            root.SetParent(parent, false);
            Dictionary<string, object> envSpec;
            string envName, envSrc;
            if (JObj(exp, "environment") != null)
            {
                envSpec = JObj(exp, "environment");
                envName = "inline";
                envSrc = "inline in " + Path.GetFileName(path);
            }
            else
            {
                envName = JStr(exp, "scene", "flat");
                envSpec = EnvironmentBuilder.Load(envName, Path.GetDirectoryName(path), out envSrc);
            }
            Env = EnvironmentBuilder.Build(envSpec, envName, envSrc, root, sceneMu);

            int seed = (int)JNum(exp, "seed", 0);
            bool noise = JBool(JObj(exp, "sensors"), "noise", true);
            Sim = new UdircSim(car, SimParams.From(car, tireMu), cfg.controller, cfg.sensors, Env, root, seed, noise);
            var st = JObj(exp, "initial_state");
            Sim.Place(JVec(st, "position", new double[] { 0, 0, double.NaN }),
                      (float)JNum(st, "yaw_deg", 0), (float)JNum(st, "pitch_deg", 0), (float)JNum(st, "roll_deg", 0),
                      (float)JNum(st, "speed", 0), JVec(st, "velocity", null), JVec(st, "angular_velocity_deg_s", null));

            var outs = JObj(exp, "outputs");
            sigEvery = Mathf.Max(1, Mathf.RoundToInt((float)(1.0 / (UdircSim.Dt * JNum(outs, "rate_hz", 200.0)))));
            truthEvery = Mathf.Max(1, Mathf.RoundToInt((float)(1.0 / (UdircSim.Dt * JNum(outs, "truth_rate_hz", 50.0)))));
            writeTruth = !Mystery && JBool(outs, "truth", true);    // a mystery car never gives its true state away
            if (Mystery) outs["truth"] = false;
            stopOffWorld = JBool(exp, "stop_if_off_world", true);

            internalCsv = new StringBuilder("t,steering_cmd,servo_angle_cmd,esc_cmd,throttle_cmd,brake_cmd,torque_cmd," +
                                            "vesc_torque_seen,current_ref_a,current_a,duty,bus_voltage,battery_current_a,shaft_torque\n");
            externalCsv = new StringBuilder(string.Join(",", Sim.sensors.Header().ToArray()) + "\n");
            truthCsv = new StringBuilder("t,x,y,z,roll,pitch,yaw,vx,vy,vz,wx,wy,wz,terrain_h,clearance," +
                                         "throttle,braking,steering,motor_angle,motor_speed\n");
            n = 0;
            Done = false;
            LogState();                                         // t = 0 sample
            Message = "running " + Name + " (" + Duration.ToString("0.###", CultureInfo.InvariantCulture) + " s), car " +
                      carInfo.name + (Mystery ? " (mystery: no truth.csv)" : "") + ", on " + envSrc;
        }

        // One 1 ms step. False once the run is over (then call Finish).
        public bool Step()
        {
            if (Done) return false;
            double t = Sim.SimT;
            Sim.Step(mode, (float)motorSig(t), (float)steerSig(t));
            if (n % sigEvery == 0) LogCommand(t);               // stamped when the command was issued
            n++;
            if (n % sigEvery == 0 || n % truthEvery == 0) LogState();

            if (Sim.SimT >= Duration - 1e-9) Done = true;
            else if (stopOffWorld && Sim.OffWorld) { Done = true; stopReason = "stopped at t = " + F(Sim.SimT) + " s: the car left the ground"; }
            else if (Sim.rb.linearVelocity.magnitude > 30f) { Done = true; stopReason = "stopped at t = " + F(Sim.SimT) + " s: speed > 30 m/s (unstable)"; }
            return !Done;
        }

        void LogCommand(double t)
        {
            UdircController c = Sim.ctrl;
            VescModel v = Sim.vesc;
            float esc = v.MaxTorque > 0f ? Mathf.Clamp(c.TorqueCmd / v.MaxTorque, -1f, 1f) : 0f;
            Row(internalCsv, t, c.SteerCmd, c.SteerCmd * Sim.MaxSteer, esc, Mathf.Max(0f, esc), Mathf.Max(0f, -esc),
                c.TorqueCmd, v.RequestSeen, v.CurrentRef, v.Current, v.Duty, v.BusVoltage, v.BatteryCurrent, v.Torque);
        }

        void LogState()
        {
            double t = Sim.SimT;
            if (n % sigEvery == 0)
            {
                List<double> r = Sim.sensors.Read(t, Sim.MotorAngle);
                Row(externalCsv, r.ToArray());
            }
            if (writeTruth && n % truthEvery == 0)
            {
                Vector3 p = UdircSim.ToExp(Sim.rb.position), v = UdircSim.ToExp(Sim.rb.linearVelocity);
                Vector3 a = Sim.Attitude(), w = Sim.BodyRates();
                float h = Env.HeightAt(p.x, p.y);
                float esc = Sim.vesc.MaxTorque > 0f ? Mathf.Clamp(Sim.vesc.Torque / Sim.vesc.MaxTorque, -1f, 1f) : 0f;
                Row(truthCsv, t, p.x, p.y, p.z, a.x, a.y, a.z, v.x, v.y, v.z, w.x, w.y, w.z, h, p.z - h,
                    Mathf.Max(0f, esc), Mathf.Max(0f, -esc), Sim.SteerApplied / Sim.MaxSteer, Sim.MotorAngle, Sim.MotorSpeed);
            }
        }

        static void Row(StringBuilder b, params double[] v)
        {
            for (int i = 0; i < v.Length; i++)
            {
                if (i > 0) b.Append(',');
                double x = System.Math.Abs(v[i]) < 1e-12 ? 0.0 : v[i];           // no denormal noise
                b.Append(double.IsNaN(x) ? "nan" : x.ToString("G7", CultureInfo.InvariantCulture));
            }
            b.Append('\n');
        }

        static string F(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        // Write the files; returns a one-line summary (also in Message).
        public string Finish()
        {
            OutDir = UniqueRunDir(Name, started);
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "internal.csv"), internalCsv.ToString());
            File.WriteAllText(Path.Combine(OutDir, "external.csv"), externalCsv.ToString());
            string truthPath = Path.Combine(OutDir, "truth.csv");
            if (writeTruth) File.WriteAllText(truthPath, truthCsv.ToString());
            else if (File.Exists(truthPath)) File.Delete(truthPath);
            File.WriteAllText(Path.Combine(OutDir, "experiment.json"), Write(exp) + "\n");
            Vector3 p = UdircSim.ToExp(Sim.rb.position);
            string where = Mystery ? ", mystery car (no truth)"            // the final pose is true state
                                   : ", final x " + F(p.x) + " y " + F(p.y) + " z " + F(p.z) + " m";
            Message = Name + ": " + (stopReason.Length > 0 ? stopReason : "done, " + F(Sim.SimT) + " s") + where + " -> " + OutDir;
            if (!Mystery && Car != null && Car.warnings.Count > 0)
                Message += "\n    car check (" + Car.name + "): " + string.Join("\n    car check: ", Car.warnings.ToArray());
            return Message;
        }

        // Every run gets its own folder: <name>_<yyyyMMdd-HHmmss>, plus _2, _3, ... if two runs
        // start in the same second, so nothing is ever overwritten.
        static string UniqueRunDir(string name, DateTime t)
        {
            string stem = Path.Combine(OutputRoot, name + "_" + t.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            string dir = stem;
            for (int k = 2; Directory.Exists(dir); k++) dir = stem + "_" + k;
            return dir;
        }

        // The newest run folder of an experiment (by name), or null if it never ran.
        public static string LatestRunDir(string name)
        {
            if (!Directory.Exists(OutputRoot)) return null;
            string best = null;
            DateTime bestT = DateTime.MinValue;
            foreach (string d in Directory.GetDirectories(OutputRoot, name + "_*"))
            {
                string rest = Path.GetFileName(d).Substring(name.Length + 1);
                if (rest.Length < 15 || !char.IsDigit(rest[0])) continue;      // another experiment's prefix
                DateTime w = Directory.GetCreationTime(d);
                if (w > bestT) { bestT = w; best = d; }
            }
            return best;
        }

        public string RunToEnd(Transform parent)
        {
            Begin(parent);
            while (Step()) { }
            return Finish();
        }

        public void Destroy()
        {
            if (root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
            else UnityEngine.Object.DestroyImmediate(root.gameObject);
            root = null;
        }

        // every *.json directly in experiments/ (not environments/), for pickers
        public static List<string> Available()
        {
            var list = new List<string>();
            string dir = Path.Combine(SimConfig.SimulationDir, "experiments");
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.json")) list.Add(f);
            list.Sort();
            return list;
        }
    }
}
