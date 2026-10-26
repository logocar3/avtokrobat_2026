// One UDIRC 1610S in a built environment, advanced one 1 ms step at a time: the core shared
// by the interactive PhysX mode (UnityPhysicsController) and the experiment runner.
//
//   Step(mode, ref, steer):  UdircController -> servo -> VescModel -> gearbox -> wheel spin +
//                            suspension + tyre forces -> Physics.Simulate(1 ms) -> sensors
//
// PhysX only moves the rigid chassis (and its body collider for crashes). Everything about
// the wheels is computed here, in the open, because Unity's WheelCollider is built for
// full-size cars: it adds a hidden ~1.5e-4 kg m^2 of spin inertia per wheel (more than this
// 42.5 mm wheel has), its tyre model swallowed most of the drive force, and its low-speed
// "sticky tyre" mode held the car still under full throttle. Per wheel and step:
//   - suspension: a ray from the top of the travel down the car's -up axis; spring + damper
//     (static load at mid travel, like before) plus a stiff bump stop at full compression;
//   - drivetrain (unity_model.drivetrain / front_diff / rear_diff): the 1610S is 4wd with a
//     locked centre shaft (no centre diff) and open diffs. Spin states: the shaft w_c (motor =
//     gear x w_c), a diff difference d per driven axle with an open diff (wheels = w_c +/- d;
//     a locked diff keeps d = 0), and a free spin per wheel on an undriven axle (rwd / fwd).
//     Integrated with the true inertias (rotor reflected through the gearbox + wheels),
//     sub-stepped at 20 kHz, driven by the VESC torque and braked by the tyre forces. A lifted
//     axle can't run away: the shaft ties it to the other axle;
//   - tyre: Pacejka-style mu N sin(C atan(B s)) for slip ratio and slip angle, combined on a
//     friction circle, plus rolling resistance, applied at the contact point;
//   - the chassis gets -dL/dt of the wheels (about the axles) and of the rotor (about the motor
//     axis), so spinning the wheels in the air rotates the car (M4 attitude control).
//
// Rigidbody frame: +z forward, +y up, +x right; origin at mid-wheelbase on the axle line. The
// visual CarView uses +x forward, +z left, and the CSVs / experiment files use x forward,
// y left, z up (the experiment frame, FrameConvert.cs).
using System.Collections.Generic;
using UnityEngine;

namespace Avtokrobat
{
    public class SimParams
    {
        public float mass, mu, spring, damper, travel, comHeight, delayMs, currentTauMs, cellVoltage;

        public static SimParams From(UdircFile car, float mu)
        {
            UdircUnityModel um = car.unity_model;
            return new SimParams
            {
                mass = um.mass_kg, mu = mu, spring = um.spring_n_per_m, damper = um.damper_ns_per_m,
                travel = um.suspension_travel_m, comHeight = um.com_height_m,
                delayMs = car.vesc.command_delay_s * 1000f, currentTauMs = car.vesc.current_time_constant_s * 1000f,
                cellVoltage = car.battery.cell_voltage,
            };
        }
    }

    public class UdircSim
    {
        public const float Dt = 0.001f;                       // physics AND control period: 1 kHz
        public static readonly Quaternion ToViewFrame = Quaternion.Euler(0f, -90f, 0f);
        const float BumpStiffness = 20000f, BumpDamping = 60f;   // N/m, N s/m at full compression

        public readonly UdircFile spec;
        public readonly SimParams prm;
        public readonly EnvironmentInfo env;
        public GameObject carGo;
        public Rigidbody rb;
        public CarView view;
        public UdircController ctrl;
        public VescModel vesc;
        public UnitySensors sensors;

        public double SimT { get; private set; }
        public float MotorAngle { get; private set; }         // true motor shaft, rad
        public float MotorSpeed { get; private set; }         // rad/s
        public float SteerApplied { get; private set; }       // rad, + = left
        public float MaxSteer { get; private set; }
        public float WheelRadius { get; private set; }
        public float Gear { get; private set; }

        // per wheel (FL, FR, RL, RR)
        public readonly float[] omega = new float[4];         // spin, rad/s, + = rolling forward
        public readonly float[] fx = new float[4], fy = new float[4], load = new float[4];   // N
        public readonly bool[] contact = new bool[4];
        readonly Vector3[] mount = new Vector3[4];            // rigidbody-local top of the travel
        readonly float[] compression = new float[4];          // m, 0 = fully extended
        readonly float[] spin = new float[4];                 // visual spin angle, deg
        readonly float wheelInertia, rotorInertia;
        readonly float[] staticLoad = new float[4];           // N per wheel, from the COM position
        readonly bool drivenF, drivenR, lockF, lockR;
        float wc, df, dr;                                     // shaft + diff difference speeds, rad/s
        readonly float[] free = new float[4];                 // undriven wheels' own spin, rad/s
        readonly UdircTire tire;

        public UdircSim(UdircFile spec, SimParams prm, PhysControllerCfg ctrlCfg, SensorsCfg sensorCfg,
                        EnvironmentInfo env, Transform parent, int seed, bool noise)
        {
            this.spec = spec;
            this.prm = prm;
            this.env = env;
            UdircUnityModel um = spec.unity_model;
            UdircGeometry g = spec.geometry_m;
            tire = um.tire ?? new UdircTire();
            WheelRadius = g.tire_radius;
            Gear = spec.motor_model.gear_ratio;
            MaxSteer = um.max_steer_deg * Mathf.Deg2Rad;
            float wb = g.wheelbase, track = g.track, r = g.tire_radius;

            carGo = new GameObject("UDIRC_1610S");
            carGo.transform.SetParent(parent, false);
            carGo.layer = 2;                                   // Ignore Raycast: suspension / lidar rays skip the car
            rb = carGo.AddComponent<Rigidbody>();
            rb.mass = prm.mass;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 200f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.sleepThreshold = 0f;                            // never sleep: a resting car must react to the next force

            // body collider (crashes / roll-overs): from the ground-clearance line to the roof
            float bottom = g.ground_clearance - r, top = g.height - r;
            var box = carGo.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f * (bottom + top), 0f);
            box.size = new Vector3(Mathf.Max(0.05f, track - g.tire_width), top - bottom, g.length - 2f * r);
            box.sharedMaterial = new PhysicsMaterial("Body") { dynamicFriction = 0.5f, staticFriction = 0.5f, bounciness = 0f };
            rb.centerOfMass = new Vector3(0f, prm.comHeight, um.com_forward_m);
            float[] I = um.inertia_rpy_kgm2;                   // roll, pitch, yaw
            if (I != null && I.Length == 3)
            {
                rb.inertiaTensor = new Vector3(I[1], I[2], I[0]);   // rigidbody axes: x pitch, y yaw, z roll
                rb.inertiaTensorRotation = Quaternion.identity;
            }

            // spin inertias: wheel + tyre ~0.7 m r^2 each; the motor rotor acts on the centre shaft
            // through the gearbox (J G^2)
            wheelInertia = 0.7f * um.wheel_mass_kg * r * r;
            rotorInertia = spec.motor_model.rotor_inertia_kgm2;
            float frontShare = 0.5f + um.com_forward_m / wb;                 // static weight on the front axle
            for (int i = 0; i < 4; i++)
                staticLoad[i] = prm.mass * -Physics.gravity.y * (i < 2 ? frontShare : 1f - frontShare) / 2f;
            drivenF = um.drivetrain != "rwd";
            drivenR = um.drivetrain != "fwd";
            lockF = um.front_diff == "locked";
            lockR = um.rear_diff == "locked";

            // FL, FR, RL, RR: the travel's top is half a travel above the axle line, so at the
            // static load (mid travel) the wheel centres sit on the axle line
            for (int i = 0; i < 4; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;          // left = -x in the rigidbody frame
                float ax = (i < 2) ? 1f : -1f;
                mount[i] = new Vector3(side * 0.5f * track, 0.5f * prm.travel, ax * 0.5f * wb);
            }

            view = SceneBuilder.BuildCar(wb, track, r, g.tire_width, parent);

            spec.vesc.command_delay_s = prm.delayMs / 1000f;
            spec.vesc.current_time_constant_s = Mathf.Max(0.01f, prm.currentTauMs) / 1000f;
            spec.battery.cell_voltage = prm.cellVoltage;
            vesc = new VescModel(spec.motor_model, spec.battery, spec.vesc);
            ctrl = new UdircController(vesc.MaxTorque, MaxSteer, ctrlCfg.speed_kp, ctrlCfg.speed_ki,
                                       ctrlCfg.speed_window, spec.motor_model.pole_pairs, um.brake_torque_fraction);
            sensors = new UnitySensors(sensorCfg, um.accel_mount_xyz, um.lidar_mount_xyz,
                                       spec.motor_model.pole_pairs, seed, noise);
        }

        // Pose + motion in the experiment frame. position z NaN = wheels on the ground below.
        // Angles in degrees (yaw + = left, pitch + = nose down, roll + = left side up), speed
        // forward [m/s] with the wheels already rolling, velocity world [m/s], angular
        // velocity body [deg/s] (x roll, y pitch, z yaw).
        public void Place(double[] pos, float yawDeg, float pitchDeg, float rollDeg,
                          float speed, double[] velocity, double[] angVelDegS)
        {
            float x = (float)pos[0], y = (float)pos[1];
            float z;
            if (pos.Length < 3 || double.IsNaN(pos[2]))
            {
                float h = env.HeightAt(x, y);
                z = (float.IsNaN(h) ? 0f : h) + WheelRadius + 0.002f;
            }
            else z = (float)pos[2];
            Quaternion view = Quaternion.AngleAxis(-yawDeg, Vector3.up) * Quaternion.AngleAxis(-pitchDeg, Vector3.forward)
                              * Quaternion.AngleAxis(-rollDeg, Vector3.right);
            carGo.transform.SetPositionAndRotation(FrameConvert.Pos(x, y, z), view * Quaternion.Inverse(ToViewFrame));
            rb.position = carGo.transform.position;
            rb.rotation = carGo.transform.rotation;
            Physics.SyncTransforms();

            Vector3 v = carGo.transform.forward * speed;
            if (velocity != null && velocity.Length >= 3) v += FrameConvert.Pos((float)velocity[0], (float)velocity[1], (float)velocity[2]);
            rb.linearVelocity = v;
            if (angVelDegS != null && angVelDegS.Length >= 3)
            {
                // body (roll, pitch, yaw) rates -> rigidbody local; the axis swap flips the sense
                var wLocal = new Vector3((float)angVelDegS[1], -(float)angVelDegS[2], -(float)angVelDegS[0]) * Mathf.Deg2Rad;
                rb.angularVelocity = rb.rotation * wLocal;
            }
            wc = speed / WheelRadius;                          // rolling, no slip
            df = dr = 0f;
            for (int i = 0; i < 4; i++) free[i] = wc;
            WheelSpeeds();
            for (int i = 0; i < 4; i++) compression[i] = -1f;  // "no previous sample" for the damper
            MotorSpeed = Gear * wc;
            SyncVisuals();
        }

        void WheelSpeeds()
        {
            if (drivenF) { omega[0] = wc + df; omega[1] = wc - df; }   // FL, FR via the front diff
            else { omega[0] = free[0]; omega[1] = free[1]; }
            if (drivenR) { omega[2] = wc + dr; omega[3] = wc - dr; }   // RL, RR via the rear diff
            else { omega[2] = free[2]; omega[3] = free[3]; }
        }

        bool Driven(int i) { return i < 2 ? drivenF : drivenR; }

        public void Step(MotorMode mode, float motorRef, float steerRefRad)
        {
            UdircUnityModel um = spec.unity_model;
            // controller (every step, 1 kHz) on the Hall-quantised motor angle
            ctrl.Step(SimT, Dt, MotorAngle, mode, motorRef, steerRefRad);
            SteerApplied = Mathf.MoveTowards(SteerApplied, ctrl.SteerCmd * MaxSteer, um.servo_rate_deg_s * Mathf.Deg2Rad * Dt);
            float shaftTorque = vesc.Step(Dt, ctrl.TorqueCmd, MotorSpeed);
            float shaftDrive = shaftTorque * Gear * um.gear_efficiency;          // on the centre shaft

            Transform tr = carGo.transform;
            Vector3 up = tr.up;
            float r = WheelRadius, travel = prm.travel, rayLen = travel + r;
            float motorBefore = MotorSpeed, wheelsBefore = omega[0] + omega[1] + omega[2] + omega[3];
            var hitPt = new Vector3[4]; var fwdDir = new Vector3[4]; var sideDir = new Vector3[4];
            var vx = new float[4]; var den = new float[4]; var fy0 = new float[4]; var cap = new float[4];

            // 1. suspension + contact frame per wheel
            for (int i = 0; i < 4; i++)
            {
                fx[i] = fy[i] = load[i] = 0f;
                contact[i] = false;
                Vector3 top = tr.TransformPoint(mount[i]);
                RaycastHit hit;
                if (!Physics.Raycast(top, -up, out hit, rayLen)) { compression[i] = 0f; continue; }
                // static load at mid travel, linear spring + damper, bump stop. The damper uses the
                // mount's own speed towards the ground, not the change in ray length: a ray that
                // steps onto a kerb or first touches a slope would read as a huge compression
                // speed and kick the car
                float x = rayLen - hit.distance;
                float xdot = -Vector3.Dot(rb.GetPointVelocity(top), up);
                compression[i] = x;
                float f = staticLoad[i] + prm.spring * (x - 0.5f * travel) + prm.damper * xdot;
                if (x > travel) f += BumpStiffness * (x - travel) + BumpDamping * xdot;
                f = Mathf.Max(0f, f);                                            // a tyre only pushes
                rb.AddForceAtPosition(up * f, hit.point, ForceMode.Force);
                float N = f * Mathf.Max(0f, Vector3.Dot(up, hit.normal));       // load normal to the ground
                if (N <= 0f) continue;
                load[i] = N;
                contact[i] = true;
                // tyre frame on the ground: wheel heading (steered) projected onto the contact plane
                Vector3 heading = i < 2 ? Quaternion.AngleAxis(-SteerApplied * Mathf.Rad2Deg, up) * tr.forward : tr.forward;
                fwdDir[i] = Vector3.ProjectOnPlane(heading, hit.normal).normalized;
                sideDir[i] = Vector3.Cross(hit.normal, fwdDir[i]);
                hitPt[i] = hit.point;
                float mu = Mathf.Min(prm.mu, env.MuOf(hit.collider, prm.mu));   // grip: min(tyre, surface)
                Vector3 vp = rb.GetPointVelocity(hit.point);
                vx[i] = Vector3.Dot(vp, fwdDir[i]);
                float vy = Vector3.Dot(vp, sideDir[i]);
                den[i] = Mathf.Max(Mathf.Abs(vx[i]), tire.v_reg);
                cap[i] = mu * N;
                // the lateral force doesn't depend on the wheel spin: once per step
                fy0[i] = -cap[i] * Mathf.Sin(tire.Cy * Mathf.Atan(tire.By * Mathf.Atan2(vy, den[i])));
            }

            // 2. drivetrain + longitudinal tyre forces, sub-stepped:
            //    (J G^2 + n_driven I_w) dw_c/dt = drive - r sum(Fx, driven wheels)
            //    2 I_w dd/dt = -r (Fx_left - Fx_right) - c_diff d     (open diff on a driven axle)
            //    I_w dw/dt = -r Fx                                       (wheel on an undriven axle)
            int n = Mathf.Max(1, tire.substeps);
            int nDriven = (drivenF ? 2 : 0) + (drivenR ? 2 : 0);
            float h = Dt / n, Ic = rotorInertia * Gear * Gear + nDriven * wheelInertia, Id = 2f * wheelInertia;
            var sx = new float[4]; var sy = new float[4]; var fi = new float[4];
            for (int k = 0; k < n; k++)
            {
                for (int i = 0; i < 4; i++)
                {
                    fi[i] = 0f;
                    if (!contact[i]) continue;
                    float kappa = (omega[i] * r - vx[i]) / den[i];
                    float fx0 = cap[i] * Mathf.Sin(tire.Cx * Mathf.Atan(tire.Bx * kappa));
                    float mag = Mathf.Sqrt(fx0 * fx0 + fy0[i] * fy0[i]);
                    float sc = mag > cap[i] ? cap[i] / mag : 1f;                 // friction circle
                    fi[i] = fx0 * sc;
                    sx[i] += fi[i];
                    sy[i] += fy0[i] * sc;
                }
                float drivenForce = (drivenF ? fi[0] + fi[1] : 0f) + (drivenR ? fi[2] + fi[3] : 0f);
                wc += h * (shaftDrive - r * drivenForce) / Ic;
                // open diffs with internal friction (greased bevel gears): a small torque against
                // the left/right speed difference keeps one wheel from running away on its own
                if (drivenF && !lockF) df += h * (-r * (fi[0] - fi[1]) - tire.diff_damping * df) / Id;
                if (drivenR && !lockR) dr += h * (-r * (fi[2] - fi[3]) - tire.diff_damping * dr) / Id;
                for (int i = 0; i < 4; i++)
                    if (!Driven(i)) free[i] += h * (-r * fi[i]) / wheelInertia;
                WheelSpeeds();
            }

            // 3. tyre forces on the chassis, at the contact points
            for (int i = 0; i < 4; i++)
            {
                if (!contact[i]) continue;
                float roll = tire.rolling_resistance * load[i] * (float)System.Math.Tanh(vx[i] / 0.05f);
                fx[i] = sx[i] / n - roll;
                fy[i] = sy[i] / n;
                rb.AddForceAtPosition(fwdDir[i] * fx[i] + sideDir[i] * fy[i], hitPt[i], ForceMode.Force);
            }
            MotorSpeed = Gear * wc;
            // the spinning parts push back on the chassis (forward roll is +x)
            float dWheel = omega[0] + omega[1] + omega[2] + omega[3] - wheelsBefore;
            Vector3 motorAxis = um.motor_axis == "transverse" ? tr.right : tr.forward;
            rb.AddTorque(-tr.right * (wheelInertia * dWheel / Dt)
                         - motorAxis * (rotorInertia * (MotorSpeed - motorBefore) / Dt), ForceMode.Force);

            Physics.Simulate(Dt);
            SimT += Dt;
            MotorAngle += MotorSpeed * Dt;
            for (int i = 0; i < 4; i++) spin[i] = (spin[i] + omega[i] * Dt * Mathf.Rad2Deg) % 360f;
            sensors.Step(SimT, Dt, rb);
        }

        public int Grounded()
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (contact[i]) n++;
            return n;
        }

        // forward speed of the chassis, m/s
        public float ForwardSpeed { get { return Vector3.Dot(rb.linearVelocity, carGo.transform.forward); } }

        public bool OffWorld { get { return rb.position.y < env.minTop - 2f; } }

        public void SyncVisuals()
        {
            Transform tr = carGo.transform;
            view.chassis.SetPositionAndRotation(tr.position, tr.rotation * ToViewFrame);
            for (int i = 0; i < 4; i++)
            {
                // wheel centre: the travel's top minus the extension (compression 0 = hanging)
                float c = Mathf.Clamp(compression[i] < 0f ? 0.5f * prm.travel : compression[i], 0f, prm.travel);
                Vector3 p = tr.TransformPoint(mount[i] - Vector3.up * (prm.travel - c));
                Quaternion steer = i < 2 ? Quaternion.AngleAxis(-SteerApplied * Mathf.Rad2Deg, Vector3.up) : Quaternion.identity;
                view.wheels[i].SetPositionAndRotation(p, tr.rotation * steer * Quaternion.AngleAxis(spin[i], Vector3.right) * ToViewFrame);
            }
        }

        // roll / pitch / yaw (rad), the CSV convention, of the chassis
        public Vector3 Attitude()
        {
            Quaternion q = carGo.transform.rotation * ToViewFrame;
            Vector3 f = ToExp(q * Vector3.right), l = ToExp(q * Vector3.forward), u = ToExp(q * Vector3.up);
            return new Vector3(Mathf.Atan2(l.z, u.z), -Mathf.Asin(Mathf.Clamp(f.z, -1f, 1f)), Mathf.Atan2(f.y, f.x));
        }

        // Unity world vector -> experiment frame (x fwd, y left, z up)
        public static Vector3 ToExp(Vector3 v) { return new Vector3(v.x, v.z, v.y); }

        // body rates (roll, pitch, yaw) [rad/s]
        public Vector3 BodyRates()
        {
            Vector3 w = Quaternion.Inverse(rb.rotation) * rb.angularVelocity;
            return new Vector3(-w.z, w.x, -w.y);
        }

        public void Destroy()
        {
            var all = new List<GameObject> { carGo, view.chassis.gameObject };
            foreach (Transform t in view.wheels) all.Add(t.gameObject);
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                go.SetActive(false);                           // out of the physics scene now
                if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            }
        }
    }
}
