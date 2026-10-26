// Minimal JSON reader/writer for the experiment and environment files. JsonUtility can't
// do these: a signal may be a number, a list of [t, v] points or an object, and fields are
// optional. Parse() gives Dictionary<string, object> / List<object> / double / string /
// bool / null; the J* helpers read them with defaults.
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Avtokrobat
{
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i < text.Length) throw Err(text, i, "unexpected text after the JSON value");
            return v;
        }

        static System.Exception Err(string s, int i, string msg)
        {
            int line = 1;
            for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new System.FormatException("JSON line " + line + ": " + msg);
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw Err(s, i, "unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Err(s, i, "expected a \"key\"");
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Err(s, i, "expected ':' after \"" + k + "\"");
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw Err(s, i, "expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw Err(s, i, "expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            if (i > start && double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            throw Err(s, start, "can't read a value here");
        }

        static string Str(string s, ref int i)
        {
            var b = new StringBuilder();
            i++;                                     // opening quote
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char e = s[i];
                    if (e == 'n') b.Append('\n');
                    else if (e == 't') b.Append('\t');
                    else if (e == 'u' && i + 4 < s.Length)
                    {
                        b.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber));
                        i += 4;
                    }
                    else b.Append(e);
                }
                else b.Append(s[i]);
                i++;
            }
            i++;                                     // closing quote
            return b.ToString();
        }

        // ---- writing (pretty, 2-space indent) ----
        public static string Write(object v)
        {
            var b = new StringBuilder();
            Write(b, v, 0);
            return b.ToString();
        }

        static void Write(StringBuilder b, object v, int ind)
        {
            if (v == null) { b.Append("null"); return; }
            if (v is bool) { b.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { b.Append('"').Append(((string)v).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append('"'); return; }
            if (v is double || v is float || v is int || v is long)
            {
                double d = System.Convert.ToDouble(v, CultureInfo.InvariantCulture);
                b.Append(double.IsInfinity(d) || double.IsNaN(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { b.Append("{}"); return; }
                b.Append("{\n");
                int n = 0;
                foreach (var kv in dict)
                {
                    b.Append(' ', ind + 2).Append('"').Append(kv.Key).Append("\": ");
                    Write(b, kv.Value, ind + 2);
                    b.Append(++n < dict.Count ? ",\n" : "\n");
                }
                b.Append(' ', ind).Append('}');
                return;
            }
            var list = v as List<object>;
            if (list != null)
            {
                bool flat = true;                    // short lists of numbers on one line
                foreach (object o in list) if (o is Dictionary<string, object> || o is List<object> && ((List<object>)o).Count > 4) flat = false;
                b.Append('[');
                for (int k = 0; k < list.Count; k++)
                {
                    if (!flat) b.Append('\n').Append(' ', ind + 2);
                    Write(b, list[k], ind + 2);
                    if (k < list.Count - 1) b.Append(flat ? ", " : ",");
                }
                if (!flat) b.Append('\n').Append(' ', ind);
                b.Append(']');
                return;
            }
            b.Append('"').Append(v.ToString()).Append('"');
        }

        // ---- typed access with defaults ----
        public static Dictionary<string, object> JObj(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> JList(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static double JNum(Dictionary<string, object> d, string key, double def)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is double ? (double)v : def;
        }

        public static string JStr(Dictionary<string, object> d, string key, string def)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is string ? (string)v : def;
        }

        public static bool JBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool ? (bool)v : def;
        }

        // [a, b, c] -> double[]; null entries become NaN ("not given", e.g. z = null)
        public static double[] JVec(Dictionary<string, object> d, string key, double[] def)
        {
            List<object> l = JList(d, key);
            if (l == null) return def;
            var r = new double[l.Count];
            for (int k = 0; k < l.Count; k++) r[k] = l[k] is double ? (double)l[k] : double.NaN;
            return r;
        }

        public static bool Has(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key);
        }

        // Recursive merge: values in 'over' win; nested objects are merged, not replaced.
        public static Dictionary<string, object> Merge(Dictionary<string, object> baseD, Dictionary<string, object> over)
        {
            var o = new Dictionary<string, object>();
            if (baseD != null) foreach (var kv in baseD) o[kv.Key] = kv.Value;
            if (over != null)
                foreach (var kv in over)
                {
                    var a = o.ContainsKey(kv.Key) ? o[kv.Key] as Dictionary<string, object> : null;
                    var b = kv.Value as Dictionary<string, object>;
                    o[kv.Key] = (a != null && b != null) ? Merge(a, b) : kv.Value;
                }
            return o;
        }
    }
}
