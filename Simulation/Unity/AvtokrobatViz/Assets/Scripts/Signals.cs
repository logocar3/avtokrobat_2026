// Experiment input signals: the same forms as src/experiment.py make_signal() and
// experiments/README.md section 5 (all times in s).
//   3.5                                   constant
//   [[t, v], [t, v], ...]                 piecewise linear through the points
//   {"type": "points", "points": [[t, v], ...], "interp": "linear" | "hold"}
//   {"type": "step", "t0", "value", "before": 0, "t1": end (optional), "after": before}
//   {"type": "ramp", "t0", "t1", "v0", "v1"}               (holds v0 before, v1 after)
//   {"type": "sine", "amp", "freq", "offset": 0, "phase_deg": 0, "t0": 0, "t1": inf}
//   {"type": "chirp", "amp", "f0", "f1", "t0", "t1", "offset": 0, "method": "linear" | "log"}
//   {"type": "prbs", "amp", "period", "t0": 0, "t1": inf, "offset": 0, "seed": 0}
//   {"type": "sum", "terms": [spec, spec, ...]}
// Outside [t0, t1] the windowed signals (sine, chirp, prbs) return their offset.
// PRBS bits come from System.Random(seed): deterministic, but not the same bits as Python.
using System;
using System.Collections.Generic;
using static Avtokrobat.MiniJson;

namespace Avtokrobat
{
    public static class Signals
    {
        public static Func<double, double> Make(object spec, string name)
        {
            if (spec is double) { double c = (double)spec; return t => c; }
            if (spec == null) return t => 0.0;
            var asList = spec as List<object>;
            var d = asList != null
                ? new Dictionary<string, object> { { "type", "points" }, { "points", asList } }
                : spec as Dictionary<string, object>;
            if (d == null || !Has(d, "type")) throw new FormatException(name + ": can't read this signal");
            string k = JStr(d, "type", "");
            if (k == "points")
            {
                var pts = new List<double[]>();
                foreach (object o in JList(d, "points") ?? new List<object>())
                {
                    var p = o as List<object>;
                    if (p == null || p.Count < 2) throw new FormatException(name + ": points must be [t, value] pairs");
                    pts.Add(new[] { (double)p[0], (double)p[1] });
                }
                if (pts.Count == 0) return t => 0.0;
                pts.Sort((a, b) => a[0].CompareTo(b[0]));
                bool hold = JStr(d, "interp", "linear") == "hold";
                return t =>
                {
                    if (t <= pts[0][0]) return pts[0][1];
                    for (int i = 1; i < pts.Count; i++)
                        if (t < pts[i][0])
                        {
                            double ta = pts[i - 1][0], va = pts[i - 1][1], tb = pts[i][0], vb = pts[i][1];
                            return hold || tb == ta ? va : va + (vb - va) * (t - ta) / (tb - ta);
                        }
                    return pts[pts.Count - 1][1];
                };
            }
            if (k == "step")
            {
                double t0 = Req(d, "t0", name), val = Req(d, "value", name), before = JNum(d, "before", 0.0);
                double t1 = JNum(d, "t1", double.PositiveInfinity), after = JNum(d, "after", before);
                return t => t < t0 ? before : (t < t1 ? val : after);
            }
            if (k == "ramp")
            {
                double t0 = Req(d, "t0", name), t1 = Req(d, "t1", name), v0 = Req(d, "v0", name), v1 = Req(d, "v1", name);
                return t => t <= t0 ? v0 : (t >= t1 ? v1 : v0 + (v1 - v0) * (t - t0) / (t1 - t0));
            }
            double w0 = JNum(d, "t0", 0.0), w1 = JNum(d, "t1", double.PositiveInfinity), off = JNum(d, "offset", 0.0);
            if (k == "sine")
            {
                double a = Req(d, "amp", name), fr = Req(d, "freq", name), ph = JNum(d, "phase_deg", 0.0) * Math.PI / 180.0;
                return t => off + (w0 <= t && t < w1 ? a * Math.Sin(2 * Math.PI * fr * (t - w0) + ph) : 0.0);
            }
            if (k == "chirp")
            {
                double a = Req(d, "amp", name), f0 = Req(d, "f0", name), f1 = Req(d, "f1", name);
                double T = Req(d, "t1", name) - Req(d, "t0", name);
                Func<double, double> phase;
                if (JStr(d, "method", "linear") == "log")
                {
                    double kk = Math.Pow(f1 / f0, 1.0 / T);
                    phase = tau => 2 * Math.PI * f0 * (Math.Pow(kk, tau) - 1) / Math.Log(kk);
                }
                else phase = tau => 2 * Math.PI * (f0 * tau + 0.5 * (f1 - f0) / T * tau * tau);
                return t => off + (w0 <= t && t < w1 ? a * Math.Sin(phase(t - w0)) : 0.0);
            }
            if (k == "prbs")
            {
                double a = Req(d, "amp", name), per = Req(d, "period", name);
                var rng = new Random((int)JNum(d, "seed", 0));
                int n = (int)Math.Ceiling(Math.Min(w1 - w0, 3600.0) / per) + 1;
                var bits = new double[n];
                for (int i = 0; i < n; i++) bits[i] = rng.Next(2) == 0 ? -1.0 : 1.0;
                return t => off + (w0 <= t && t < w1 ? a * bits[Math.Min(n - 1, (int)((t - w0) / per))] : 0.0);
            }
            if (k == "sum")
            {
                var fs = new List<Func<double, double>>();
                foreach (object o in JList(d, "terms") ?? new List<object>()) fs.Add(Make(o, name));
                return t => { double s = 0; foreach (var f in fs) s += f(t); return s; };
            }
            throw new FormatException(name + ": unknown signal type '" + k + "'");
        }

        static double Req(Dictionary<string, object> d, string key, string name)
        {
            if (!Has(d, key) || !(d[key] is double)) throw new FormatException(name + ": '" + key + "' is required for type '" + JStr(d, "type", "") + "'");
            return (double)d[key];
        }
    }
}
