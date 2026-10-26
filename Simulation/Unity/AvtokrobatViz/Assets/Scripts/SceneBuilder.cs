// Builds the visible car: a primitive-built look-alike of the UDIRC 1610S "Darksteel" monster
// truck (black pickup shell, orange/gold side stripes) on plain cylinder wheels. Proportions
// come from side-view product photos and are scaled to the car's own geometry: lengths by
// wheelbase, heights by wheel radius, widths by track. Purely visual (no colliders); UdircSim
// places it on the simulated chassis and wheels every frame.
using UnityEngine;

namespace Avtokrobat
{
    // Handles to the visual transforms UdircSim drives each frame.
    public class CarView
    {
        public Transform chassis;
        public Transform[] wheels;   // FL, FR, RL, RR
        public Transform shell;      // body + frame, child of chassis, origin = axle midpoint
    }

    public static class SceneBuilder
    {
        // UDIRC 1610S livery
        static readonly Color BodyBlack = new Color(0.035f, 0.035f, 0.04f);
        static readonly Color FrameGrey = new Color(0.20f, 0.20f, 0.22f);
        static readonly Color TrimGrey = new Color(0.11f, 0.11f, 0.12f);
        static readonly Color Glass = new Color(0.06f, 0.08f, 0.10f);
        static readonly Color Orange = new Color(0.98f, 0.52f, 0.13f);
        static readonly Color Gold = new Color(0.86f, 0.78f, 0.46f);
        static readonly Color Amber = new Color(1.00f, 0.62f, 0.15f);
        static readonly Color TailRed = new Color(0.75f, 0.05f, 0.04f);
        static readonly Color Wheel = new Color(0.09f, 0.09f, 0.10f);

        // wb = wheelbase, track = wheel-centre track, r = wheel radius, w = wheel width (m)
        public static CarView BuildCar(float wb, float track, float r, float w, Transform parent)
        {
            var view = new CarView();
            var chassisRoot = new GameObject("Chassis").transform;
            chassisRoot.SetParent(parent, false);
            view.shell = new GameObject("Shell").transform;
            view.shell.SetParent(chassisRoot, false);
            BuildShell(view.shell, wb, track, r);

            string[] names = { "wheel_FL", "wheel_FR", "wheel_RL", "wheel_RR" };
            view.wheels = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var wheelRoot = new GameObject(names[i]).transform;
                wheelRoot.SetParent(parent, false);
                BuildWheel(wheelRoot, r, w);
                view.wheels[i] = wheelRoot;
            }
            view.chassis = chassisRoot;
            return view;
        }

        // Shell-local frame (Unity): +x forward, +y up, +z left; origin at the axle midpoint,
        // axle height. X stations are in wheelbases, heights in wheel radii.
        static void BuildShell(Transform s, float W, float T, float R)
        {
            float hb = 0.35f * T;                       // body half-width: clears the tyres
            Material black = Mat(BodyBlack, 0.85f), grey = Mat(FrameGrey, 0.3f),
                     trim = Mat(TrimGrey, 0.4f), glass = Mat(Glass, 0.95f),
                     orange = Mat(Orange, 0.6f), gold = Mat(Gold, 0.6f),
                     amber = Glow(Amber, 1.6f), red = Glow(TailRed, 1.2f);

            // --- frame: skid plate, bumpers, grey running boards ---
            Span(s, "Skid", -0.50f * W, 0.58f * W, -0.25f * R, 0.20f * R, 0.22f * T, grey);
            Span(s, "BumperF", 0.60f * W, 0.70f * W, -0.05f * R, 0.40f * R, 0.27f * T, grey);
            Span(s, "BumperR", -0.70f * W, -0.62f * W, 0.05f * R, 0.40f * R, 0.25f * T, grey);
            Span(s, "Step", -0.30f * W, 0.25f * W, 0.05f * R, 0.25f * R, hb + 0.01f * T, grey);

            // --- pickup body: bed, cab, hood ---
            Span(s, "BedLower", -0.64f * W, -0.20f * W, 0.20f * R, 0.70f * R, hb, black);
            for (int side = -1; side <= 1; side += 2)
                Box(s, "BedWall", new Vector3(-0.42f * W, 0.945f * R, side * (hb - 0.025f * T)),
                    new Vector3(0.44f * W, 0.49f * R, 0.05f * T), black);
            Span(s, "Tailgate", -0.64f * W, -0.60f * W, 0.70f * R, 1.15f * R, hb, black);
            Span(s, "Cab", -0.20f * W, 0.24f * W, 0.20f * R, 1.19f * R, hb, black);
            Span(s, "Hood", 0.24f * W, 0.62f * W, 0.20f * R, 1.08f * R, hb, black);
            Span(s, "HoodScoop", 0.32f * W, 0.50f * W, 1.08f * R, 1.14f * R, 0.4f * hb, trim);

            // --- greenhouse: glass, slanted windshield, roof, pillars ---
            Span(s, "Glass", -0.17f * W, 0.12f * W, 1.19f * R, 1.56f * R, 0.88f * hb, glass);
            Slant(s, "Windshield", new Vector2(0.12f * W, 1.56f * R), new Vector2(0.27f * W, 1.19f * R),
                  0.02f * R, 0.88f * hb, glass);
            Span(s, "Roof", -0.18f * W, 0.13f * W, 1.56f * R, 1.66f * R, 0.92f * hb, black);
            foreach (float px in new[] { -0.165f * W, 0.0f })
                for (int side = -1; side <= 1; side += 2)
                    Box(s, "Pillar", new Vector3(px, 1.375f * R, side * (0.88f * hb + 0.003f)),
                        new Vector3(0.03f * W, 0.37f * R, 0.006f), black);

            // --- fender flares over each axle ---
            for (int ax = -1; ax <= 1; ax += 2)
                for (int side = -1; side <= 1; side += 2)
                    Box(s, "Flare", new Vector3(ax * 0.5f * W, 1.13f * R, side * (hb + 0.06f * T)),
                        new Vector3(2.0f * R, 0.14f * R, 0.12f * T), trim);

            // --- livery: slanted orange + gold bands, pinstripe, door number plate ---
            const float tilt = 25f;                     // band tops lean rearward
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (hb + 0.001f);
                Box(s, "StripeOrange", new Vector3(-0.09f * W, 0.72f * R, z),
                    new Vector3(0.12f * W, 1.05f * R, 0.002f), orange, Quaternion.Euler(0f, 0f, tilt));
                Box(s, "StripeGold", new Vector3(0.0f, 0.45f * R, z),
                    new Vector3(0.04f * W, 0.45f * R, 0.002f), gold, Quaternion.Euler(0f, 0f, tilt));
                Box(s, "Pinstripe", new Vector3(0.22f * W, 0.40f * R, z),
                    new Vector3(0.62f * W, 0.06f * R, 0.002f), orange);
                Box(s, "Number", new Vector3(0.25f * W, 0.88f * R, z),
                    new Vector3(0.08f * W, 0.26f * R, 0.002f), orange);
            }

            // --- nose and tail: grille, headlights, taillights ---
            float nose = 0.62f * W + 0.004f, tail = -0.64f * W - 0.004f;
            Box(s, "Grille", new Vector3(nose, 0.70f * R, 0f), new Vector3(0.008f, 0.50f * R, hb), trim);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(s, "Headlight", new Vector3(nose, 0.86f * R, side * 0.75f * hb),
                    new Vector3(0.008f, 0.12f * R, 0.36f * hb), amber);
                Box(s, "Taillight", new Vector3(tail, 0.90f * R, side * 0.85f * hb),
                    new Vector3(0.008f, 0.30f * R, 0.24f * hb), red);
            }

            // --- wheelie bar with two small rollers ---
            Span(s, "WheelieBar", -0.78f * W, -0.62f * W, 0.32f * R, 0.42f * R, 0.04f * T, grey);
            for (int side = -1; side <= 1; side += 2)
                Cyl(s, "WheelieRoller", new Vector3(-0.78f * W, 0.37f * R, side * 0.10f * T),
                    0.16f * R, 0.10f * R, Mat(Wheel, 0.2f));
        }

        // Plain cylinder at the physics radius; wheel-root spin axis = local +z.
        static void BuildWheel(Transform root, float R, float w)
        {
            Cyl(root, "WheelMesh", Vector3.zero, R, w, Mat(Wheel, 0.2f));
        }

        // ---------------------------------------------------------------- primitive helpers

        static void Box(Transform parent, string name, Vector3 center, Vector3 size, Material m,
                        Quaternion? rot = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        // Axis-aligned box from x/y extents, centred laterally with the given half-width.
        static void Span(Transform parent, string name, float x0, float x1, float y0, float y1,
                         float halfWidth, Material m)
        {
            Box(parent, name, new Vector3(0.5f * (x0 + x1), 0.5f * (y0 + y1), 0f),
                new Vector3(x1 - x0, y1 - y0, 2f * halfWidth), m);
        }

        // Thin panel from p0 to p1 in the x/y plane (e.g. a raked windshield).
        static void Slant(Transform parent, string name, Vector2 p0, Vector2 p1, float thick,
                          float halfWidth, Material m)
        {
            Vector2 d = p1 - p0;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Box(parent, name, 0.5f * (p0 + p1), new Vector3(d.magnitude, thick, 2f * halfWidth), m,
                Quaternion.Euler(0f, 0f, ang));
        }

        // Cylinder whose axis is local +z.
        static void Cyl(Transform parent, string name, Vector3 center, float radius, float length, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            // Unity cylinder axis is local +Y (height 2); tip it 90 deg about X onto +Z.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(2f * radius, 0.5f * length, 2f * radius);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void StripCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;                 // pure visuals; never part of the physics
            // Destroy is deferred to end of frame: disable now so PhysX, which may step before
            // then, never collides with a visual part
            c.enabled = false;
            if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
        }

        static Material Mat(Color c, float gloss, float metallic = 0f)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = c;
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        static Material Glow(Color c, float intensity)
        {
            Material m = Mat(c, 0.8f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            return m;
        }

        static void Paint(GameObject go, Color c, float gloss)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = Mat(c, gloss);
        }
    }
}
