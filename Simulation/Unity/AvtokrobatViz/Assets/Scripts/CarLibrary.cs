// Car files: find them, load them (plain cars/<name>.json or encrypted mystery cars/<name>.car),
// check them, and summarise what they will do.
//
//   cars/udirc_1610s.json   the reference car (the real UDIRC 1610S)
//   cars/<name>.json        a student's car: a copy of the reference with changed numbers
//   cars/<name>.car         a mystery car made by the teacher (teacher/make-mystery-car.ps1):
//                           the same JSON, AES-encrypted. The simulator runs it but never shows
//                           its parameters, and experiments with it write no truth.csv.
//
// The encryption only keeps the numbers out of sight: the key is in this file, so a determined
// student with the source could decrypt a .car. That is the agreed trade-off (no separate build).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Avtokrobat
{
    public class CarInfo
    {
        public string name, source, description;
        public bool mystery;
        public readonly List<string> errors = new List<string>(), warnings = new List<string>();
    }

    public static class CarLibrary
    {
        public const string Reference = "udirc_1610s";
        public const string MysteryHeader = "AVTOKROBAT-MYSTERY-CAR 1";
        // shared with teacher/make-mystery-car.ps1 (AES-256-CBC, key = SHA-256 of this text)
        const string Passphrase = "avtokrobat/mystery-car/v1: identify me, don't decrypt me";

        public static string CarsDir { get { return Path.Combine(SimConfig.SimulationDir, "cars"); } }

        // names of every car in cars/ (reference first); mystery cars keep their .car suffix
        public static List<string> Available()
        {
            var names = new List<string> { Reference };
            if (Directory.Exists(CarsDir))
            {
                var files = new List<string>(Directory.GetFiles(CarsDir, "*.json"));
                files.Sort();
                foreach (string f in files)
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (!names.Contains(n)) names.Add(n);
                }
                var myst = new List<string>(Directory.GetFiles(CarsDir, "*.car"));
                myst.Sort();
                foreach (string f in myst) names.Add(Path.GetFileName(f));
            }
            return names;
        }

        // nameOrPath: "udirc_1610s", "my_car", "mystery_A.car", or a path (tried as given, next
        // to relativeTo, then in cars/). overrides: any subset of the car JSON (not for mystery cars).
        public static UdircFile Load(string nameOrPath, Dictionary<string, object> overrides, string relativeTo, out CarInfo info)
        {
            if (string.IsNullOrEmpty(nameOrPath)) nameOrPath = Reference;
            string path = Find(nameOrPath, relativeTo);
            if (path == null)
                throw new FileNotFoundException("car '" + nameOrPath + "' not found in " + CarsDir +
                    " (use a file name from cars/, e.g. \"" + Reference + "\")");
            info = new CarInfo { source = path };
            string text = File.ReadAllText(path);
            if (text.StartsWith(MysteryHeader))
            {
                info.mystery = true;
                if (overrides != null && overrides.Count > 0)
                    throw new InvalidOperationException("a mystery car can't be changed: remove \"car_overrides\" / \"tire_mu\"");
                text = Decrypt(text);
            }
            var d = MiniJson.Parse(text) as Dictionary<string, object>;
            if (d == null) throw new FormatException(Path.GetFileName(path) + ": a car file must contain one JSON object");
            if (overrides != null && overrides.Count > 0) { d = MiniJson.Merge(d, overrides); info.source += " + overrides"; }
            UdircFile car = JsonUtility.FromJson<UdircFile>(MiniJson.Write(d));
            info.name = car.name.Length > 0 ? car.name : Path.GetFileNameWithoutExtension(path);
            info.description = info.mystery ? "Mystery car: its parameters are hidden. Identify them from experiments."
                                             : car.description;
            if (info.mystery) info.name = Path.GetFileName(path);
            Check(car, info);
            if (info.errors.Count > 0)
                throw new FormatException(Path.GetFileName(path) + ": " + string.Join("; ", info.errors.ToArray()));
            return car;
        }

        static string Find(string nameOrPath, string relativeTo)
        {
            var tries = new List<string>();
            if (Path.IsPathRooted(nameOrPath)) tries.Add(nameOrPath);
            else
            {
                if (!string.IsNullOrEmpty(relativeTo)) tries.Add(Path.Combine(relativeTo, nameOrPath));
                tries.Add(Path.Combine(CarsDir, nameOrPath));
            }
            foreach (string t in tries)
                foreach (string p in new[] { t, t + ".json", t + ".car" })
                    if (File.Exists(p)) return Path.GetFullPath(p);
            return null;
        }

        // ---------------------------------------------------------------- mystery cars
        static byte[] Key() { using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(Passphrase)); }

        public static string Encrypt(string json)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                aes.GenerateIV();
                byte[] plain = Encoding.UTF8.GetBytes(json), body;
                using (var enc = aes.CreateEncryptor()) body = enc.TransformFinalBlock(plain, 0, plain.Length);
                var all = new byte[aes.IV.Length + body.Length];
                Buffer.BlockCopy(aes.IV, 0, all, 0, aes.IV.Length);
                Buffer.BlockCopy(body, 0, all, aes.IV.Length, body.Length);
                return MysteryHeader + "\n" + Convert.ToBase64String(all, Base64FormattingOptions.InsertLineBreaks) + "\n";
            }
        }

        public static string Decrypt(string fileText)
        {
            string b64 = fileText.Substring(MysteryHeader.Length).Replace("\r", "").Replace("\n", "").Trim();
            byte[] all = Convert.FromBase64String(b64);
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                var iv = new byte[16];
                Buffer.BlockCopy(all, 0, iv, 0, 16);
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                    return Encoding.UTF8.GetString(dec.TransformFinalBlock(all, 16, all.Length - 16));
            }
        }

        // ---------------------------------------------------------------- checks
        public static void Check(UdircFile c, CarInfo info)
        {
            var E = info.errors; var W = info.warnings;
            UdircGeometry g = c.geometry_m;
            UdircUnityModel u = c.unity_model;
            UdircMotorModel m = c.motor_model;
            Pos(E, "geometry_m.wheelbase", g.wheelbase); Pos(E, "geometry_m.track", g.track);
            Pos(E, "geometry_m.tire_radius", g.tire_radius); Pos(E, "geometry_m.tire_width", g.tire_width);
            Pos(E, "geometry_m.length", g.length); Pos(E, "geometry_m.height", g.height);
            Pos(E, "unity_model.mass_kg", u.mass_kg); Pos(E, "unity_model.wheel_mass_kg", u.wheel_mass_kg);
            Pos(E, "unity_model.suspension_travel_m", u.suspension_travel_m);
            Pos(E, "unity_model.spring_n_per_m", u.spring_n_per_m);
            if (u.damper_ns_per_m < 0f) E.Add("unity_model.damper_ns_per_m must be >= 0");
            Pos(E, "motor_model.kv_rpm_per_v", m.kv_rpm_per_v); Pos(E, "motor_model.resistance_ohm", m.resistance_ohm);
            Pos(E, "motor_model.gear_ratio", m.gear_ratio); Pos(E, "motor_model.max_current_a", m.max_current_a);
            if (m.rotor_inertia_kgm2 < 0f) E.Add("motor_model.rotor_inertia_kgm2 must be >= 0");
            if (m.pole_pairs < 1) E.Add("motor_model.pole_pairs must be >= 1");
            if (c.battery.cells < 1) E.Add("battery.cells must be >= 1");
            if (c.battery.cell_voltage < 2.5f || c.battery.cell_voltage > 4.35f) E.Add("battery.cell_voltage must be 2.5 .. 4.35 V");
            if (u.gear_efficiency <= 0f || u.gear_efficiency > 1f) E.Add("unity_model.gear_efficiency must be in (0, 1]");
            if (u.max_steer_deg <= 0f || u.max_steer_deg >= 60f) E.Add("unity_model.max_steer_deg must be in (0, 60)");
            if (u.drivetrain != "4wd" && u.drivetrain != "rwd" && u.drivetrain != "fwd") E.Add("unity_model.drivetrain must be \"4wd\", \"rwd\" or \"fwd\"");
            foreach (string d in new[] { u.front_diff, u.rear_diff })
                if (d != "open" && d != "locked") E.Add("unity_model.front_diff / rear_diff must be \"open\" or \"locked\"");
            if (u.motor_axis != "longitudinal" && u.motor_axis != "transverse") E.Add("unity_model.motor_axis must be \"longitudinal\" or \"transverse\"");
            if (Mathf.Abs(u.com_forward_m) >= 0.5f * g.wheelbase) E.Add("unity_model.com_forward_m puts the centre of mass outside the wheelbase");
            if (u.com_height_m + g.tire_radius <= 0f) E.Add("unity_model.com_height_m puts the centre of mass below the ground");
            float[] I = u.inertia_rpy_kgm2;
            if (I == null || I.Length != 3 || I[0] <= 0f || I[1] <= 0f || I[2] <= 0f)
                E.Add("unity_model.inertia_rpy_kgm2 must be three positive numbers [roll, pitch, yaw]");
            else if (I[0] + I[1] < I[2] || I[1] + I[2] < I[0] || I[0] + I[2] < I[1])
                E.Add("unity_model.inertia_rpy_kgm2 is not a possible rigid body (each value must be <= the sum of the other two)");
            if (E.Count > 0) return;

            // plausibility warnings: the car runs, but the numbers look odd
            float mass = u.mass_kg;
            float bx = g.length, by = g.width, bz = g.height;           // a solid box of the car's size
            float[] box = { mass * (by * by + bz * bz) / 12f, mass * (bx * bx + bz * bz) / 12f, mass * (bx * bx + by * by) / 12f };
            string[] axis = { "roll", "pitch", "yaw" };
            for (int k = 0; k < 3; k++)
            {
                float ratio = I[k] / box[k];
                if (ratio < 0.25f || ratio > 2.5f)
                    W.Add(string.Format(CultureInfo.InvariantCulture, "{0} inertia {1:0.0000} kg m^2 is {2:0.00}x a solid box of the car's size and mass ({3:0.0000})", axis[k], I[k], ratio, box[k]));
            }
            float f = SuspensionHz(c), z = SuspensionZeta(c);
            if (f < 1.5f || f > 15f) W.Add(string.Format(CultureInfo.InvariantCulture, "suspension natural frequency {0:0.0} Hz is unusual for an RC car (typ. 3-8 Hz)", f));
            if (z < 0.15f || z > 1.5f) W.Add(string.Format(CultureInfo.InvariantCulture, "suspension damping ratio {0:0.00} is unusual (typ. 0.3-0.8)", z));
            if (u.wheel_mass_kg > 0.25f * mass) W.Add("each wheel is more than a quarter of the car's mass");
            if (g.ground_clearance > 2f * g.tire_radius) W.Add("ground clearance is larger than the wheel diameter");
            if (m.max_current_a > c.vesc.motor_current_max_a * 1.01f) W.Add("motor_model.max_current_a is above vesc.motor_current_max_a: the VESC limit wins");
            if (u.tire != null && (u.tire.Cx > 1.9f || u.tire.Cy > 1.9f)) W.Add("tyre shape factor C > 1.9 gives a strange slip curve (typ. 1.2-1.6)");
        }

        static void Pos(List<string> E, string name, float v) { if (!(v > 0f)) E.Add(name + " must be > 0"); }

        // ---------------------------------------------------------------- spec sheet
        static float SprungPerWheel(UdircFile c) { return Mathf.Max(1e-3f, c.unity_model.mass_kg / 4f); }
        public static float SuspensionHz(UdircFile c) { return Mathf.Sqrt(c.unity_model.spring_n_per_m / SprungPerWheel(c)) / (2f * Mathf.PI); }
        public static float SuspensionZeta(UdircFile c)
        {
            return c.unity_model.damper_ns_per_m / (2f * Mathf.Sqrt(c.unity_model.spring_n_per_m * SprungPerWheel(c)));
        }

        // What the numbers imply. mu = tyre grip used by the run (min of tyre and surface).
        public static List<string> SpecSheet(UdircFile c, float mu)
        {
            var L = new List<string>();
            UdircGeometry g = c.geometry_m; UdircUnityModel u = c.unity_model; UdircMotorModel m = c.motor_model;
            float gr = 9.81f, mass = u.mass_kg, r = g.tire_radius, G = m.gear_ratio;
            float kvRad = m.kv_rpm_per_v * 2f * Mathf.PI / 60f, kt = 1f / kvRad;
            float vBus = c.battery.cells * c.battery.cell_voltage;
            float iMax = Mathf.Min(m.max_current_a, c.vesc.motor_current_max_a);
            float vTop = c.vesc.max_duty * vBus * kvRad / G * r;                    // no-load, flat
            float fMotor = kt * iMax * G * u.gear_efficiency / r;
            float frontShare = 0.5f + u.com_forward_m / g.wheelbase;
            float driven = u.drivetrain == "4wd" ? 1f : (u.drivetrain == "fwd" ? frontShare : 1f - frontShare);
            float fGrip = mu * driven * mass * gr;
            float aPeak = Mathf.Min(fMotor, fGrip) / mass;
            float h = r + u.com_height_m;
            float[] I = u.inertia_rpy_kgm2;
            float iw = 0.7f * u.wheel_mass_kg * r * r;
            float wheelSpin = 4f * iw + (u.motor_axis == "transverse" ? m.rotor_inertia_kgm2 * G * G : 0f);
            L.Add(string.Format(CultureInfo.InvariantCulture, "mass {0:0.000} kg, wheelbase {1:0.000} m, track {2:0.000} m, tyre r {3:0.0000} m", mass, g.wheelbase, g.track, r));
            L.Add(string.Format(CultureInfo.InvariantCulture, "static load front/rear {0:0}% / {1:0}%, COM {2:0.000} m above ground", 100f * frontShare, 100f * (1f - frontShare), h));
            L.Add(string.Format(CultureInfo.InvariantCulture, "drivetrain {0}, diffs front {1} / rear {2}", u.drivetrain, u.front_diff, u.rear_diff));
            L.Add(string.Format(CultureInfo.InvariantCulture, "motor {0:0} KV: Kt {1:0.00000} N m/A, max {2:0.000} N m at {3:0} A; {4}S {5:0.00} V", m.kv_rpm_per_v, kt, kt * iMax, iMax, c.battery.cells, vBus));
            L.Add(string.Format(CultureInfo.InvariantCulture, "top speed (no load, flat) {0:0.0} m/s = {1:0} km/h", vTop, vTop * 3.6f));
            L.Add(string.Format(CultureInfo.InvariantCulture, "max drive force {0:0.0} N (motor) vs {1:0.0} N (grip mu {2:0.00}) -> peak accel {3:0.0} m/s^2 = {4:0.00} g{5}",
                fMotor, fGrip, mu, aPeak, aPeak / gr, fMotor > fGrip ? " (wheelspin at full throttle)" : ""));
            L.Add(string.Format(CultureInfo.InvariantCulture, "suspension {0:0.0} Hz, damping ratio {1:0.00}, travel {2:0.000} m", SuspensionHz(c), SuspensionZeta(c), u.suspension_travel_m));
            L.Add(string.Format(CultureInfo.InvariantCulture, "min turning radius {0:0.00} m (steer {1:0} deg); rolls over above ~{2:0.00} g lateral",
                g.wheelbase / Mathf.Tan(u.max_steer_deg * Mathf.Deg2Rad), u.max_steer_deg, g.track / (2f * h)));
            if (I != null && I.Length == 3)
                L.Add(string.Format(CultureInfo.InvariantCulture, "in-air pitch: spinning parts / chassis inertia = {0:0.000}; wheels +100 rad/s -> chassis {1:0.00} rad/s",
                    wheelSpin / I[1], 100f * wheelSpin / I[1]));
            return L;
        }
    }
}
