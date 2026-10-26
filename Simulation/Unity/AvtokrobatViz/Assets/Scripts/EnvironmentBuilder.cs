// Builds the world the car drives on from a small JSON description (experiments/environments/
// *.json, or inline in an experiment). Coordinates are the experiment frame: x forward,
// y left, z up, metres. A piece is one of:
//
//   {"type": "ground", "x": [-3, 6], "width": 8}                  flat slab, top at z (default 0)
//   {"type": "ramp", "x": [6, 10.14], "z": [0, 1.11], "width": 8}  slope (or plateau / landing)
//                                                                  from (x0, z0) to (x1, z1)
//   {"type": "box", "center": [5, 1, 0.05], "size": [0.3, 0.3, 0.1]}   obstacle
//
// Optional on every piece: "y" (centre, ground/ramp), "yaw_deg" (about the piece centre; a box
// also takes "pitch_deg", "roll_deg"), "color" [r, g, b], "mu" (grip of that surface; the tyre
// gets min(tyre mu, piece mu)), "thickness" (ground, default 0.5), "bottom" (ramp underside,
// default min(z0, z1) - 0.5), "name".
// File level: "description", "pieces": [...], "start": {"position": [x, y, z|null], "yaw_deg"}.
// "flat" and "jump" are built in (a file with that name in experiments/environments wins).
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Avtokrobat.MiniJson;

namespace Avtokrobat
{
    public class EnvironmentInfo
    {
        public string name, description, source;
        public Transform root;
        public readonly Dictionary<Collider, float> mu = new Dictionary<Collider, float>();
        public double[] startPosition = { 0, 0, double.NaN };   // NaN z = on the ground
        public float startYawDeg;
        public float minTop = 0f;                                // lowest surface: off-world below it

        // Height of the highest surface under (x, y) (experiment frame), or NaN if none.
        public float HeightAt(float x, float y)
        {
            Vector3 from = FrameConvert.Pos(x, y, 1000f);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 3000f);
            float best = float.NaN;
            foreach (RaycastHit h in hits)
                if (mu.ContainsKey(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return best;
        }

        public float MuOf(Collider c, float fallback)
        {
            float m;
            return c != null && mu.TryGetValue(c, out m) ? m : fallback;
        }
    }

    public static class EnvironmentBuilder
    {
        public const string Flat = "{\"description\": \"200 m x 200 m flat ground\"," +
            "\"pieces\": [{\"type\": \"ground\", \"x\": [-100, 100], \"width\": 200}]}";

        // The M1 jump: run-up, 15 deg ramp to a 1.11 m lip,
        // 1.67 m gap, landing slope, 20 m run-out, all 8 m wide.
        public const string Jump = "{\"description\": \"M1 jump: run-up, 15 deg ramp, 1.67 m gap, landing slope, run-out\"," +
            "\"pieces\": [" +
            "{\"type\": \"ground\", \"name\": \"run-up\", \"x\": [-3, 6], \"width\": 8}," +
            "{\"type\": \"ramp\", \"name\": \"take-off\", \"x\": [6, 10.144], \"z\": [0, 1.1104], \"width\": 8, \"color\": [0.85, 0.5, 0.35]}," +
            "{\"type\": \"ramp\", \"name\": \"landing\", \"x\": [11.81, 15.954], \"z\": [1.1104, 0], \"width\": 8, \"color\": [0.85, 0.5, 0.35]}," +
            "{\"type\": \"ground\", \"name\": \"run-out\", \"x\": [15.954, 35.954], \"width\": 8}]}";

        static readonly Color DefaultColor = new Color(0.7f, 0.7f, 0.7f);

        public static string EnvironmentsDir
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "experiments", "environments")); }
        }

        // "flat" / "jump" / a file name or path (tried as given, next to relativeTo, then in
        // experiments/environments, with or without .json).
        public static Dictionary<string, object> Load(string nameOrPath, string relativeTo, out string source)
        {
            var tries = new List<string>();
            if (Path.IsPathRooted(nameOrPath)) tries.Add(nameOrPath);
            else
            {
                if (!string.IsNullOrEmpty(relativeTo)) tries.Add(Path.Combine(relativeTo, nameOrPath));
                tries.Add(Path.Combine(EnvironmentsDir, nameOrPath));
            }
            foreach (string t in tries)
                foreach (string p in new[] { t, t + ".json" })
                    if (File.Exists(p))
                    {
                        source = Path.GetFullPath(p);
                        return (Dictionary<string, object>)Parse(File.ReadAllText(p));
                    }
            source = "built-in '" + nameOrPath + "'";
            if (nameOrPath == "flat") return (Dictionary<string, object>)Parse(Flat);
            if (nameOrPath == "jump") return (Dictionary<string, object>)Parse(Jump);
            throw new FileNotFoundException("environment '" + nameOrPath + "' not found (looked in " +
                string.Join(", ", tries.ToArray()) + ")");
        }

        // Every *.json in experiments/environments plus the built-ins, for pickers.
        public static List<string> Available()
        {
            var names = new List<string> { "jump", "flat" };
            if (Directory.Exists(EnvironmentsDir))
                foreach (string f in Directory.GetFiles(EnvironmentsDir, "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (!names.Contains(n)) names.Add(n);
                }
            return names;
        }

        public static EnvironmentInfo Build(Dictionary<string, object> spec, string name, string source,
                                            Transform parent, float defaultMu)
        {
            var env = new EnvironmentInfo { name = name, source = source };
            env.description = JStr(spec, "description", "");
            env.root = new GameObject("Environment " + name).transform;
            env.root.SetParent(parent, false);
            var start = JObj(spec, "start");
            if (start != null)
            {
                env.startPosition = JVec(start, "position", env.startPosition);
                env.startYawDeg = (float)JNum(start, "yaw_deg", 0);
            }
            List<object> pieces = JList(spec, "pieces");
            if (pieces == null || pieces.Count == 0) throw new System.FormatException("environment '" + name + "' has no \"pieces\"");
            bool first = true;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i] as Dictionary<string, object>;
                if (p == null) throw new System.FormatException("environment '" + name + "': piece " + i + " is not an object");
                string type = JStr(p, "type", "");
                GameObject go;
                float top;
                if (type == "ground") go = Ground(p, out top);
                else if (type == "ramp") go = Ramp(p, out top);
                else if (type == "box") go = Box(p, out top);
                else throw new System.FormatException("environment '" + name + "': piece " + i + " has unknown type '" + type + "' (use ground, ramp or box)");
                go.name = JStr(p, "name", type + "_" + i);
                go.transform.SetParent(env.root, true);
                double[] col = JVec(p, "color", null);
                Color c = col != null && col.Length >= 3 ? new Color((float)col[0], (float)col[1], (float)col[2])
                                                          : (type == "box" ? new Color(0.35f, 0.4f, 0.75f) : DefaultColor);
                var m = new Material(Shader.Find("Standard"));
                m.color = c;
                m.SetFloat("_Glossiness", 0.05f);
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                env.mu[go.GetComponent<Collider>()] = (float)JNum(p, "mu", defaultMu);
                if (type != "box") { env.minTop = first ? top : Mathf.Min(env.minTop, top); first = false; }
            }
            Physics.SyncTransforms();
            return env;
        }

        static double[] Pair(Dictionary<string, object> p, string key, double a, double b)
        {
            double[] v = JVec(p, key, null);
            return v != null && v.Length >= 2 ? v : new[] { a, b };
        }

        static GameObject Ground(Dictionary<string, object> p, out float top)
        {
            double[] x = Pair(p, "x", -100, 100);
            float w = (float)JNum(p, "width", 200), y = (float)JNum(p, "y", 0);
            float z = (float)JNum(p, "z", 0), th = (float)JNum(p, "thickness", 0.5);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = FrameConvert.Pos(0.5f * (float)(x[0] + x[1]), y, z - 0.5f * th);
            go.transform.rotation = Yaw((float)JNum(p, "yaw_deg", 0));
            go.transform.localScale = FrameConvert.Size((float)System.Math.Abs(x[1] - x[0]), w, th);
            top = z;
            return go;
        }

        static GameObject Box(Dictionary<string, object> p, out float top)
        {
            double[] c = JVec(p, "center", new double[] { 0, 0, 0.05 });
            double[] s = JVec(p, "size", new double[] { 0.2, 0.2, 0.1 });
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = FrameConvert.Pos((float)c[0], (float)c[1], (float)c[2]);
            // yaw (about z, + = left) / pitch (about y, + = nose down) / roll (about x)
            float yaw = (float)JNum(p, "yaw_deg", 0), pitch = (float)JNum(p, "pitch_deg", 0), roll = (float)JNum(p, "roll_deg", 0);
            go.transform.rotation = Yaw(yaw) * Quaternion.AngleAxis(-pitch, Vector3.forward) * Quaternion.AngleAxis(-roll, Vector3.right);
            go.transform.localScale = FrameConvert.Size((float)s[0], (float)s[1], (float)s[2]);
            top = (float)(c[2] + 0.5 * s[2]);
            return go;
        }

        // yaw about the experiment z axis (+ = left) as a Unity rotation (about -Y)
        static Quaternion Yaw(float deg) { return Quaternion.AngleAxis(-deg, Vector3.up); }

        // A slope as a solid prism: top edge from (x0, z0) to (x1, z1), flat underside.
        static GameObject Ramp(Dictionary<string, object> p, out float top)
        {
            double[] x = Pair(p, "x", 0, 4), z = Pair(p, "z", 0, 1);
            float w = (float)JNum(p, "width", 8), y = (float)JNum(p, "y", 0);
            float bottom = (float)JNum(p, "bottom", System.Math.Min(z[0], z[1]) - 0.5);
            float cx = 0.5f * (float)(x[0] + x[1]);
            float hx = 0.5f * (float)(x[1] - x[0]), hy = 0.5f * w;
            // corners relative to the centre (cx, y, 0), experiment frame
            var c = new Vector3[8];
            int k = 0;
            for (int s = 0; s < 2; s++)                       // 0 = x0 end, 1 = x1 end
                for (int side = 0; side < 2; side++)          // 0 = right (-y), 1 = left (+y)
                {
                    float lx = s == 0 ? -hx : hx, ly = side == 0 ? -hy : hy;
                    float zt = (float)(s == 0 ? z[0] : z[1]);
                    c[k++] = FrameConvert.Pos(lx, ly, zt);      // top
                    c[k++] = FrameConvert.Pos(lx, ly, bottom);  // bottom
                }
            // corner index: end*4 + side*2 + (0 top, 1 bottom)
            int[][] faces =
            {
                new[] { 0, 2, 6, 4 },   // top
                new[] { 1, 5, 7, 3 },   // bottom
                new[] { 0, 1, 3, 2 },   // x0 end
                new[] { 4, 6, 7, 5 },   // x1 end
                new[] { 0, 4, 5, 1 },   // right side
                new[] { 2, 3, 7, 6 },   // left side
            };
            Vector3 centre = Vector3.zero;
            foreach (Vector3 v in c) centre += v / 8f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (int[] f in faces)
            {
                Vector3 a = c[f[0]], b = c[f[1]], cc = c[f[2]], d = c[f[3]];
                Vector3 fc = (a + b + cc + d) / 4f;
                if (Vector3.Dot(Vector3.Cross(b - a, cc - a), fc - centre) < 0f) { Vector3 t = b; b = d; d = t; }
                int o = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(cc); verts.Add(d);
                tris.Add(o); tris.Add(o + 1); tris.Add(o + 2);
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 3);
            }
            var mesh = new Mesh { name = "ramp" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("ramp");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.transform.position = FrameConvert.Pos(cx, y, 0f);
            go.transform.rotation = Yaw((float)JNum(p, "yaw_deg", 0));
            top = (float)System.Math.Min(z[0], z[1]);
            return go;
        }
    }
}
