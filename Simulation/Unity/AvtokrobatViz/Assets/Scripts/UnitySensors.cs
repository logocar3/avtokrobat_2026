// The car's sensors in the Unity engine: the same models and external.csv columns as
// src/sensors.py (accelerometer, Hall motor sensor, 8x8 lidar), stepped every 1 ms.
// Body frame for all outputs: x forward, y left, z up (the experiment / CSV convention).
//
//   accelerometer: specific force R^T (a - g) at its mount, first-order low-pass, + noise
//   Hall sensor:   3 Hall bits from the true motor angle (6 states per electrical rev),
//                  a signed step counter and the ESC's speed estimate between reads
//   lidar:         8x8 zones, one ray per zone centre, at rate_hz; values held in between,
//                  NaN = no return. Rays ignore the car itself (it's on Ignore Raycast).
using System.Collections.Generic;
using UnityEngine;

namespace Avtokrobat
{
    [System.Serializable]
    public class AccelCfg { public float noise_std = 0.05f, bandwidth_hz = 50f; }

    [System.Serializable]
    public class LidarCfg
    {
        public int zones = 8;
        public float fov_deg = 45f, rate_hz = 15f, max_range = 4f, min_range = 0.02f, noise_std = 0.005f, pitch_down_deg = 15f;
    }

    [System.Serializable]
    public class SensorsCfg
    {
        public int log_every = 5, seed;
        public AccelCfg accelerometer = new AccelCfg();
        public LidarCfg lidar = new LidarCfg();
    }

    public class UnitySensors
    {
        static readonly int[][] HallSeq = { new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 0, 1, 0 },
                                            new[] { 0, 1, 1 }, new[] { 0, 0, 1 }, new[] { 1, 0, 1 } };
        const double Sector = System.Math.PI / 3.0;

        readonly SensorsCfg cfg;
        readonly Vector3 accMountLocal, lidarMountLocal;     // rigidbody-local
        readonly Quaternion lidarMountRot;                    // sensor -> rigidbody-local
        readonly List<Vector3> lidarDirs = new List<Vector3>(); // rigidbody-local unit vectors
        readonly int polePairs;
        readonly bool noise;
        System.Random rng;

        Vector3 accF;                                         // filtered specific force, body (fwd, left, up)
        bool accInit;
        Vector3 prevPointVel;
        bool havePrevVel;
        int lidarFrame;
        double lidarNext;
        float[] ranges;
        long prevCount;
        double prevT = double.NaN;

        public int Rows { get { return cfg.lidar.zones; } }

        // mounts are in the experiment body frame (x fwd, y left, z up) relative to the
        // rigidbody origin (mid-wheelbase, axle line)
        public UnitySensors(SensorsCfg cfg, float[] accMount, float[] lidarMount, int polePairs, int seed, bool noise)
        {
            this.cfg = cfg;
            this.polePairs = Mathf.Max(1, polePairs);
            this.noise = noise;
            rng = new System.Random(seed);
            accMountLocal = BodyToLocal(accMount);
            lidarMountLocal = BodyToLocal(lidarMount);
            // tilted down by pitch_down_deg: about the body's left axis = rigidbody -x
            lidarMountRot = Quaternion.AngleAxis(cfg.lidar.pitch_down_deg, Vector3.right);
            int n = cfg.lidar.zones;
            float fov = cfg.lidar.fov_deg * Mathf.Deg2Rad;
            for (int r = 0; r < n; r++)
            {
                float el = fov / 2f - (r + 0.5f) * fov / n;
                for (int c = 0; c < n; c++)
                {
                    float az = fov / 2f - (c + 0.5f) * fov / n;
                    // body (1, tan az, tan el) -> rigidbody local (x = -left, y = up, z = fwd)
                    Vector3 d = new Vector3(-Mathf.Tan(az), Mathf.Tan(el), 1f).normalized;
                    lidarDirs.Add(lidarMountRot * d);
                }
            }
            ranges = new float[n * n];
            Reset();
        }

        static Vector3 BodyToLocal(float[] b)
        {
            if (b == null || b.Length < 3) return Vector3.zero;
            return new Vector3(-b[1], b[2], b[0]);
        }

        public static Vector3 LocalToBody(Vector3 v) { return new Vector3(v.z, -v.x, v.y); }

        public void Reset()
        {
            accInit = havePrevVel = false;
            accF = Vector3.zero;
            lidarFrame = -1;
            lidarNext = 0.0;
            for (int i = 0; i < ranges.Length; i++) ranges[i] = float.NaN;
            prevCount = 0;
            prevT = double.NaN;
        }

        // every physics step, after Physics.Simulate
        public void Step(double t, float dt, Rigidbody rb)
        {
            Vector3 mount = rb.transform.TransformPoint(accMountLocal);
            Vector3 v = rb.GetPointVelocity(mount);
            if (havePrevVel && dt > 0f)
            {
                Vector3 a = (v - prevPointVel) / dt;
                Vector3 f = LocalToBody(Quaternion.Inverse(rb.rotation) * (a - Physics.gravity));
                if (!accInit || cfg.accelerometer.bandwidth_hz <= 0f) { accF = f; accInit = true; }
                else accF += (1f - Mathf.Exp(-2f * Mathf.PI * cfg.accelerometer.bandwidth_hz * dt)) * (f - accF);
            }
            prevPointVel = v;
            havePrevVel = true;

            if (t + 1e-9 >= lidarNext)
            {
                lidarNext += 1.0 / cfg.lidar.rate_hz;
                lidarFrame++;
                Vector3 o = rb.transform.TransformPoint(lidarMountLocal);
                for (int i = 0; i < lidarDirs.Count; i++)
                {
                    RaycastHit hit;
                    float r = float.NaN;
                    if (Physics.Raycast(o, rb.rotation * lidarDirs[i], out hit, cfg.lidar.max_range))
                    {
                        r = hit.distance + Gauss(cfg.lidar.noise_std);
                        if (r < cfg.lidar.min_range || r > cfg.lidar.max_range) r = float.NaN;
                    }
                    ranges[i] = r;
                }
            }
        }

        float Gauss(float sigma)
        {
            if (!noise || sigma <= 0f) return 0f;
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        public List<string> Header()
        {
            var h = new List<string> { "t", "acc_x", "acc_y", "acc_z", "hall_a", "hall_b", "hall_c", "hall_state",
                                       "hall_count", "motor_pos_hall", "motor_speed_hall", "lidar_frame" };
            for (int r = 0; r < Rows; r++) for (int c = 0; c < Rows; c++) h.Add("lidar_r" + r + "c" + c);
            return h;
        }

        // one external.csv row (advances the Hall speed estimate and the noise: call once per row)
        public List<double> Read(double t, float motorAngle)
        {
            float s = cfg.accelerometer.noise_std;
            long count = (long)System.Math.Floor(motorAngle * polePairs / Sector);
            int state = (int)(((count % 6) + 6) % 6);
            double pos = count * Sector / polePairs;
            double spd = (double.IsNaN(prevT) || t <= prevT) ? 0.0 : (count - prevCount) * Sector / polePairs / (t - prevT);
            prevCount = count;
            prevT = t;
            var row = new List<double>
            {
                t, accF.x + Gauss(s), accF.y + Gauss(s), accF.z + Gauss(s),
                HallSeq[state][0], HallSeq[state][1], HallSeq[state][2], state, count, pos, spd, lidarFrame
            };
            foreach (float r in ranges) row.Add(r);
            return row;
        }

        public Vector3 Acceleration { get { return accF; } }
        public float[] Ranges { get { return ranges; } }
        public int LidarFrame { get { return lidarFrame; } }
    }
}
