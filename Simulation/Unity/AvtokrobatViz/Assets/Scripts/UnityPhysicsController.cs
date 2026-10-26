// The simulator: drive the UDIRC 1610S (UdircSim: car + VESC + 1 kHz controller +
// sensors) in an environment built from JSON, or run experiment files and watch them.
//
//   Environment: pick one (experiments/environments/*.json + built-in jump / flat). Reset
//                reloads the files, so edit an environment JSON and press Reset to see it.
//   Drive:       Scripted (config/scene.json driver schedule, throttle phase as a speed target)
//                or Keyboard (W throttle, S brake then reverse, A/D steer).
//   Experiments: pick a file from experiments/, Run (watch) at the chosen rate or Run (fast);
//                CSVs go to output/unity_experiments/<name>_<time>/ (same as the headless runner).
//
// Physics and control share one 1 kHz clock, stepped by hand (Physics.Simulate) so Run /
// Pause / Step / Reset and the rate selector work and runs are repeatable.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Avtokrobat
{
    public class UnityPhysicsController : MonoBehaviour
    {
        const float Dt = UdircSim.Dt;
        const int MaxStepsPerFrame = 400, FastStepsPerFrame = 4000;

        // config (reloaded on Reset)
        PhysConfigFile cfg;
        UdircFile spec;
        List<Vector2> steerSchedule = new List<Vector2>();
        string cfgSource, carSource;
        SimParams prm;                              // slider values; null = take them from the spec
        float launchSpeed = -1f;
        string error;

        // car (cars/*.json, mystery cars cars/*.car)
        List<string> carNames;
        int carIdx;
        string loadedCar;                           // the car the sliders belong to
        CarInfo carInfo;
        bool showSpec;

        // world
        Transform worldRoot;
        UdircSim sim;
        List<string> envNames;
        int envIdx;
        string envNote = "";

        // stepping
        SimulationMode prevSimMode;
        float acc;
        bool running;
        static readonly string[] RateLabels = { "0.1x", "0.25x", "0.5x", "1x", "2x" };
        static readonly float[] RateValues = { 0.1f, 0.25f, 0.5f, 1f, 2f };
        int rateIdx = 3;

        // driver
        static readonly string[] DriverLabels = { "Scripted (scene.json schedule)", "Keyboard (W/A/S/D)" };
        int driverIdx;
        MotorMode motorMode;
        float motorRef, steerRefRad, keyTorque;

        // experiments
        List<string> expFiles = new List<string>();
        int expIdx;
        ExperimentRunner runner;
        bool fast;
        string expNote = "";

        // first-jump summary (wheel contact based)
        bool wasAir, launched, landed;
        float launchT, launchSpeedMeas, apexY, landT, landX;

        // camera orbit (same behaviour as the other viewers)
        Camera cam;
        float camYaw = 0f, camPitch = 14f, camDist = 1.5f;
        const float OrbitSpeed = 4f, ZoomSpeed = 8f;

        Rect panel;
        GUIStyle rich, header, mono;
        Vector2 scroll;

        UdircSim Active { get { return runner != null ? runner.Sim : sim; } }
        // mystery car on screen: show only what the real car could measure
        bool Hidden { get { return runner != null ? runner.Mystery : (carInfo != null && carInfo.mystery); } }

        // ---------------------------------------------------------------- setup
        void Awake()
        {
            prevSimMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            SetupCameraAndLight();
            envNames = EnvironmentBuilder.Available();
            expFiles = ExperimentRunner.Available();
            carNames = CarLibrary.Available();
            ResetDrive();
        }

        void OnDestroy()
        {
            ClearWorld();
            Physics.simulationMode = prevSimMode;
        }

        void ClearWorld()
        {
            if (runner != null) { runner.Destroy(); runner = null; }
            if (worldRoot != null) { worldRoot.gameObject.SetActive(false); Destroy(worldRoot.gameObject); }
            worldRoot = null;
            sim = null;
        }

        // (Re)load the config files and the chosen environment, and put the car at the start.
        void ResetDrive()
        {
            ClearWorld();
            error = null;
            acc = 0f;
            wasAir = launched = landed = false;
            try
            {
                steerSchedule.Clear();
                cfg = SimConfig.LoadScene(steerSchedule, out cfgSource);
                string carName = carNames[Mathf.Clamp(carIdx, 0, carNames.Count - 1)];
                carInfo = null;
                spec = CarLibrary.Load(carName, null, null, out carInfo);
                carSource = carInfo.mystery ? carInfo.name + " (mystery car)" : carInfo.source;
                if (carName != loadedCar) { prm = null; launchSpeed = -1f; loadedCar = carName; }   // sliders follow the car
                if (prm == null) prm = SimParams.From(spec, Mathf.Min(spec.unity_model.tire.mu, cfg.scene.friction));
                if (launchSpeed < 0f) launchSpeed = spec.unity_model.launch_speed_m_s;

                worldRoot = new GameObject("World (PhysX)").transform;
                string envName = envNames[Mathf.Clamp(envIdx, 0, envNames.Count - 1)];
                string src;
                var envSpec = EnvironmentBuilder.Load(envName, null, out src);
                EnvironmentInfo env = EnvironmentBuilder.Build(envSpec, envName, src, worldRoot, cfg.scene.friction);
                envNote = (env.description.Length > 0 ? env.description + "\n" : "") + src;

                sim = new UdircSim(spec, prm, cfg.controller, cfg.sensors, env, worldRoot, cfg.sensors.seed, true);
                bool envStart = !(env.startPosition[0] == 0 && env.startPosition[1] == 0 && double.IsNaN(env.startPosition[2]) && env.startYawDeg == 0f);
                double[] pos = envStart ? env.startPosition
                             : new double[] { cfg.vehicle.init_xyz[0], cfg.vehicle.init_xyz[1], double.NaN };
                sim.Place(pos, envStart ? env.startYawDeg : cfg.vehicle.init_yaw_deg, 0f, 0f, 0f, null, null);
            }
            catch (System.Exception e)
            {
                error = e.Message;
                Debug.LogError("[Avtokrobat] PhysX mode: " + e);
            }
        }

        void StartExperiment(bool fastRun)
        {
            if (expFiles.Count == 0) return;
            ClearWorld();
            error = null;
            fast = fastRun;
            wasAir = launched = landed = false;
            worldRoot = new GameObject("World (experiment)").transform;
            runner = new ExperimentRunner(expFiles[Mathf.Clamp(expIdx, 0, expFiles.Count - 1)]);
            try
            {
                runner.Begin(worldRoot);
                expNote = runner.Message + (runner.Description.Length > 0 ? "\n" + runner.Description : "");
                running = true;
            }
            catch (System.Exception e)
            {
                expNote = "<color=#FF6347>" + Path.GetFileName(expFiles[expIdx]) + ": " + e.Message + "</color>";
                runner.Destroy();
                runner = null;
                ResetDrive();
            }
        }

        // ---------------------------------------------------------------- stepping
        void Update()
        {
            HandleKeys();
            UdircSim s = Active;
            if (s != null && running)
            {
                if (runner != null && fast)
                {
                    for (int k = 0; k < FastStepsPerFrame && running; k++) StepOnce();
                }
                else
                {
                    acc += Time.unscaledDeltaTime * RateValues[rateIdx];
                    int n = 0;
                    while (acc >= Dt && n++ < MaxStepsPerFrame && running) { StepOnce(); acc -= Dt; }
                    if (n >= MaxStepsPerFrame) acc = 0f;     // can't keep up: drop the backlog
                }
            }
            if (Active != null) Active.SyncVisuals();
            HandleCameraInput();
            UpdateCamera();
        }

        void StepOnce()
        {
            if (runner != null)
            {
                if (!runner.Step())
                {
                    running = false;
                    try { expNote = runner.Finish(); }
                    catch (System.Exception e) { expNote = "<color=#FF6347>could not write results: " + e.Message + "</color>"; }
                }
                TrackJump(runner.Sim);
                return;
            }
            if (driverIdx == 0) ScriptedInputs((float)sim.SimT);
            else { motorMode = MotorMode.Torque; motorRef = keyTorque * sim.vesc.MaxTorque; }
            sim.Step(motorMode, motorRef, steerRefRad);
            TrackJump(sim);
            if (driverIdx == 0 && sim.SimT >= cfg.sim.duration) running = false;
            if (sim.OffWorld) running = false;
        }

        // the scene.json driver schedule, with the throttle phase as a speed target (the small
        // car would otherwise overshoot the landing at full torque)
        void ScriptedInputs(float t)
        {
            PhysDriverCfg d = cfg.driver;
            steerRefRad = Interp(steerSchedule, t) * sim.MaxSteer;
            float launchMotor = launchSpeed / sim.WheelRadius * sim.Gear;
            if (t < d.settle_time) { motorMode = MotorMode.Torque; motorRef = 0f; }
            else if (t < d.throttle_off_time) { motorMode = MotorMode.Speed; motorRef = launchMotor; }
            else if (t < d.brake_time) { motorMode = MotorMode.Torque; motorRef = 0f; }
            else { motorMode = MotorMode.Speed; motorRef = 0f; }                 // brake to a stop
        }

        static float Interp(List<Vector2> pts, float t)
        {
            if (pts.Count == 0) return 0f;
            if (t <= pts[0].x) return pts[0].y;
            for (int i = 1; i < pts.Count; i++)
                if (t <= pts[i].x)
                {
                    Vector2 a = pts[i - 1], b = pts[i];
                    return b.x > a.x ? a.y + (b.y - a.y) * (t - a.x) / (b.x - a.x) : b.y;
                }
            return pts[pts.Count - 1].y;
        }

        void TrackJump(UdircSim s)
        {
            bool air = s.Grounded() == 0 && s.SimT > 0.05;
            Vector3 p = s.rb.position;
            if (air && !wasAir && !launched)
            {
                launched = true;
                launchT = (float)s.SimT;
                launchSpeedMeas = s.rb.linearVelocity.magnitude;
                apexY = p.y;
            }
            if (launched && !landed)
            {
                if (air) apexY = Mathf.Max(apexY, p.y);
                else { landed = true; landT = (float)s.SimT; landX = p.x; }
            }
            wasAir = air;
        }

        // ---------------------------------------------------------------- input + camera
        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) running = !running;
            if (Input.GetKeyDown(KeyCode.R) && runner == null) { ResetDrive(); running = true; }
            if (Input.GetKeyDown(KeyCode.Alpha1)) { camYaw = 0f; camPitch = 14f; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { camYaw = 90f; camPitch = 12f; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { camYaw = -55f; camPitch = 22f; }
            if (Input.GetKeyDown(KeyCode.Alpha4)) { camYaw = 0.01f; camPitch = 82f; }

            if (driverIdx != 1 || sim == null) return;
            // like an RC ESC: W full forward torque; S brakes (brake_torque_fraction) while the car
            // still rolls forward, and reverses (reverse_torque_fraction) once it has stopped
            UdircUnityModel um = sim.spec.unity_model;
            float v = sim.ForwardSpeed;
            bool w = Input.GetKey(KeyCode.W), s = Input.GetKey(KeyCode.S);
            if (w) keyTorque = v < -0.3f ? um.brake_torque_fraction : 1f;
            else if (s) keyTorque = v > 0.3f ? -um.brake_torque_fraction : -um.reverse_torque_fraction;
            else keyTorque = 0f;
            float target = Input.GetKey(KeyCode.A) ? sim.MaxSteer : (Input.GetKey(KeyCode.D) ? -sim.MaxSteer : 0f);
            steerRefRad = Mathf.MoveTowards(steerRefRad, target, 3f * sim.MaxSteer * Time.unscaledDeltaTime);
        }

        void HandleCameraInput()
        {
            var gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (panel.Contains(gui)) return;
            if (Input.GetMouseButton(0))
            {
                camYaw += Input.GetAxis("Mouse X") * OrbitSpeed;
                camPitch = Mathf.Clamp(camPitch - Input.GetAxis("Mouse Y") * OrbitSpeed, -85f, 85f);
            }
            float sc = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(sc) > 1e-4f)
                camDist = Mathf.Clamp(camDist - sc * ZoomSpeed * camDist, 0.2f, 300f);
        }

        void SetupCameraAndLight()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.70f, 0.86f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 2000f;
            if (FindAnyObjectByType<Light>() == null)
            {
                var lgo = new GameObject("Sun");
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.05f;
                light.shadows = LightShadows.Soft;
                lgo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.56f);
        }

        void UpdateCamera()
        {
            UdircSim s = Active;
            if (cam == null || s == null) return;
            Vector3 target = s.view.chassis.position;
            Vector3 offset = Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist);
            cam.transform.position = target + offset;
            cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
        }

        // ---------------------------------------------------------------- GUI
        void OnGUI()
        {
            if (rich == null)
            {
                rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                header = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold };
                mono = new GUIStyle(GUI.skin.label) { richText = true, font = Font.CreateDynamicFontFromOSFont("Consolas", 12) };
            }
            panel = new Rect(10, 10, 400, Screen.height - 20);
            GUI.Box(panel, "");
            GUILayout.BeginArea(new Rect(panel.x + 8, panel.y + 6, panel.width - 16, panel.height - 12));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<size=14>Avtokrobat simulator</size>", header);
            GUILayout.Label("<size=10>car: " + carSource + "\nscene: " + cfgSource +
                            "\nphysics + control: 1 kHz (dt 1 ms)</size>", rich);
            if (error != null) GUILayout.Label("<color=#FF6347>" + error + "</color>", rich);

            if (runner == null) DrawDrive();
            GUILayout.Space(6);
            DrawExperiments();
            if (Active != null)
            {
                GUILayout.Space(6);
                DrawTelemetry(Active);
            }
            GUILayout.Space(6);
            GUILayout.Label("<size=10>Drag (outside panel): orbit   Wheel: zoom   1-4: views   Space: run/pause   R: reset+run</size>", rich);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawCar()
        {
            GUILayout.Label("Car <size=10>(cars/*.json, mystery cars *.car; Reset reloads)</size>", header);
            int nc = GUILayout.SelectionGrid(Mathf.Clamp(carIdx, 0, carNames.Count - 1), carNames.ToArray(), 2);
            if (nc != carIdx) { carIdx = nc; ResetDrive(); running = false; }
            if (carInfo == null) return;
            if (carInfo.description.Length > 0) GUILayout.Label("<size=10>" + carInfo.description + "</size>", rich);
            foreach (string w in carInfo.warnings) GUILayout.Label("<size=10><color=#FFB347>check: " + w + "</color></size>", rich);
            if (carInfo.mystery || spec == null) return;
            showSpec = GUILayout.Toggle(showSpec, " Spec sheet (what these numbers imply)");
            if (showSpec)
                GUILayout.Label("<size=10>" + string.Join("\n", CarLibrary.SpecSheet(spec, prm != null ? prm.mu : 1.1f).ToArray()) + "</size>", rich);
        }

        void DrawDrive()
        {
            GUILayout.Space(6);
            DrawCar();
            GUILayout.Space(4);
            GUILayout.Label("Environment <size=10>(experiments/environments/*.json; Reset reloads)</size>", header);
            int ne = GUILayout.SelectionGrid(envIdx, envNames.ToArray(), 3);
            if (ne != envIdx) { envIdx = ne; ResetDrive(); running = false; }
            if (envNote.Length > 0) GUILayout.Label("<size=10>" + envNote + "</size>", rich);

            GUILayout.Label("Simulation", header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(running ? "Pause" : "Run")) running = !running;
            if (GUILayout.Button("Step 10 ms") && sim != null) { running = false; for (int i = 0; i < 10; i++) StepOnce(); }
            if (GUILayout.Button("Reset"))
            {
                envNames = EnvironmentBuilder.Available();
                carNames = CarLibrary.Available();
                ResetDrive();
                running = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rate", GUILayout.Width(40));
            rateIdx = GUILayout.Toolbar(rateIdx, RateLabels);
            GUILayout.EndHorizontal();
            driverIdx = GUILayout.Toolbar(driverIdx, DriverLabels);
            if (prm == null) return;

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Parameters <size=10>(applied on Reset)</size>", header);
            if (GUILayout.Button("From files", GUILayout.Width(80))) { prm = null; launchSpeed = -1f; ResetDrive(); running = false; return; }
            GUILayout.EndHorizontal();
            launchSpeed = Slider("Scripted launch speed [m/s]", launchSpeed, 1f, 20f, "0.0");
            if (Hidden) { GUILayout.Label("<i>mystery car: its parameters are hidden</i>", rich); return; }
            prm.mass = Slider("Mass [kg]", prm.mass, 0.5f, 3f, "0.000");
            prm.mu = Slider("Tyre grip mu", prm.mu, 0.2f, 2f, "0.00");
            prm.spring = Slider("Spring / wheel [N/m]", prm.spring, 50f, 3000f, "0");
            prm.damper = Slider("Damper / wheel [N s/m]", prm.damper, 0.5f, 60f, "0.0");
            prm.travel = Slider("Suspension travel [m]", prm.travel, 0.005f, 0.06f, "0.000");
            prm.comHeight = Slider("COM above axle [m]", prm.comHeight, -0.02f, 0.06f, "0.000");
            prm.delayMs = Slider("VESC command delay [ms]", prm.delayMs, 0f, 20f, "0.0");
            prm.currentTauMs = Slider("VESC current loop tau [ms]", prm.currentTauMs, 0.05f, 10f, "0.00");
            prm.cellVoltage = Slider("Battery cell voltage [V]", prm.cellVoltage, 3.3f, 4.2f, "0.00");
        }

        void DrawExperiments()
        {
            GUILayout.Label("Experiments <size=10>(experiments/*.json -> output/unity_experiments/)</size>", header);
            if (expFiles.Count == 0) GUILayout.Label("no experiment files found in experiments/", rich);
            else
            {
                var names = new string[expFiles.Count];
                for (int i = 0; i < names.Length; i++) names[i] = Path.GetFileNameWithoutExtension(expFiles[i]);
                expIdx = GUILayout.SelectionGrid(Mathf.Clamp(expIdx, 0, names.Length - 1), names, 2);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Run (watch)")) StartExperiment(false);
            if (GUILayout.Button("Run (fast)")) StartExperiment(true);
            if (runner != null && GUILayout.Button("Back to driving")) { ClearWorld(); ResetDrive(); running = false; }
            if (GUILayout.Button("Rescan", GUILayout.Width(60))) { expFiles = ExperimentRunner.Available(); envNames = EnvironmentBuilder.Available(); }
            GUILayout.EndHorizontal();
            if (runner != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(running ? "Pause" : (runner.Done ? "Finished" : "Continue")) && !runner.Done) running = !running;
                GUILayout.Label("Rate", GUILayout.Width(36));
                rateIdx = GUILayout.Toolbar(rateIdx, RateLabels);
                GUILayout.EndHorizontal();
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "t {0:0.000} / {1:0.###} s{2}",
                    runner.Sim.SimT, runner.Duration, fast ? "   (fast)" : ""), rich);
            }
            if (expNote.Length > 0) GUILayout.Label("<size=10>" + expNote + "</size>", rich);
            // the finished run's folder, else the selected experiment's newest run, else the root
            string folder = ExperimentRunner.OutputRoot;
            if (runner != null && runner.OutDir != null) folder = runner.OutDir;
            else if (expFiles.Count > 0)
            {
                string sel = ExperimentRunner.LatestRunDir(
                    Path.GetFileNameWithoutExtension(expFiles[Mathf.Clamp(expIdx, 0, expFiles.Count - 1)]));
                if (sel != null) folder = sel;
            }
            if (GUILayout.Button("Open results in folder"))
            {
                Directory.CreateDirectory(folder);
                Application.OpenURL(new System.Uri(folder).AbsoluteUri);
            }
            GUILayout.Label("<size=10>" + folder + "</size>", rich);
        }

        float Slider(string label, float v, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + "  " + v.ToString(fmt, CultureInfo.InvariantCulture), GUILayout.Width(210));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return v;
        }

        void DrawTelemetry(UdircSim s)
        {
            if (Hidden)
            {
                // only what the car itself measures / its electronics report
                Vector3 a3 = s.sensors.Acceleration;
                GUILayout.Label("Telemetry <size=10>(mystery car: sensors and VESC only)</size>", header);
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                    "t {0,8:0.000} s   accel {1,6:0.00} {2,6:0.00} {3,6:0.00} m/s2   lidar frame {4}\n" +
                    "hall speed {5,7:0} rad/s   steer cmd {6,5:0.00}\n" +
                    "torque request {7,7:0.0000} N m   VESC sees {8,7:0.0000}\n" +
                    "current {9,6:0.0} A   duty {10,5:0.00}   battery {11,5:0.00} V {12,6:0.0} A",
                    s.SimT, a3.x, a3.y, a3.z, s.sensors.LidarFrame, s.ctrl.SpeedEstimate, s.ctrl.SteerCmd,
                    s.ctrl.TorqueCmd, s.vesc.RequestSeen, s.vesc.Current, s.vesc.Duty, s.vesc.BusVoltage, s.vesc.BatteryCurrent), mono);
                return;
            }
            GUILayout.Label("Telemetry", header);
            Vector3 p = UdircSim.ToExp(s.rb.position);
            Vector3 att = s.Attitude() * Mathf.Rad2Deg;
            Vector3 acc3 = s.sensors.Acceleration;
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "t {0,8:0.000} s   x {1,7:0.000}  y {2,6:0.000}  z {3,6:0.000} m\n" +
                "speed {4,6:0.00} m/s  roll {5,6:0.0}  pitch {6,6:0.0}  yaw {7,6:0.0} deg\n" +
                "wheels on ground {8}/4   steer {9,5:0.0} deg\n" +
                "accel {10,6:0.00} {11,6:0.00} {12,6:0.00} m/s2   lidar frame {13}",
                s.SimT, p.x, p.y, p.z, s.rb.linearVelocity.magnitude, att.x, att.y, att.z,
                s.Grounded(), s.SteerApplied * Mathf.Rad2Deg, acc3.x, acc3.y, acc3.z, s.sensors.LidarFrame), mono);

            GUILayout.Label("Controller (1 kHz) + VESC", header);
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "hall speed {0,7:0} rad/s   motor {1,7:0} rpm\n" +
                "torque request {2,7:0.0000} N m   VESC sees {3,7:0.0000}\n" +
                "current ref {4,6:0.0} A   actual {5,6:0.0} A   duty {6,5:0.00}\n" +
                "shaft torque {7,7:0.0000} N m   battery {8,5:0.00} V {9,6:0.0} A",
                s.ctrl.SpeedEstimate, s.MotorSpeed * 60f / (2f * Mathf.PI), s.ctrl.TorqueCmd, s.vesc.RequestSeen,
                s.vesc.CurrentRef, s.vesc.Current, s.vesc.Duty, s.vesc.Torque, s.vesc.BusVoltage, s.vesc.BatteryCurrent), mono);

            GUILayout.Label("First jump", header);
            if (!launched) GUILayout.Label("not airborne yet", rich);
            else GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "launch t {0:0.000} s at {1:0.00} m/s   apex {2:0.000} m\n" +
                (landed ? "air time {3:0.000} s   landed at x {4:0.00} m" : "in the air..."),
                launchT, launchSpeedMeas, apexY, landT - launchT, landX), rich);
        }
    }
}
