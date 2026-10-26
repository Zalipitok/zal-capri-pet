using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Capri
{
    // Small JSON helpers over the serializer that ships with .NET Framework, so the exe needs no
    // extra DLLs. Parsed values are Dictionary<string,object>, object[]/ArrayList, string, bool, numbers.
    static class Json
    {
        static JavaScriptSerializer NewSerializer() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        public static object Parse(string text) => NewSerializer().DeserializeObject(text);

        public static object Load(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            return Parse(text);
        }

        public static Dictionary<string, object> LoadDict(string path) => Load(path) as Dictionary<string, object>;

        public static Dictionary<string, object> Dict(object o) => o as Dictionary<string, object>;

        public static object Get(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        public static List<object> List(object o)
        {
            var list = new List<object>();
            if (o == null) return list;
            if (o is string) { list.Add(o); return list; }
            var e = o as IEnumerable;
            if (e != null && !(o is Dictionary<string, object>)) { foreach (var x in e) list.Add(x); return list; }
            list.Add(o);
            return list;
        }

        public static bool IsNumber(object o) => o is int || o is long || o is double || o is decimal || o is float;

        public static double Num(object o, double fallback = 0)
        {
            if (o == null) return fallback;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            if (o is double) return (double)o;
            if (o is decimal) return (double)(decimal)o;
            if (o is float) return (float)o;
            if (o is bool) return (bool)o ? 1 : 0;
            double d;
            if (o is string && double.TryParse((string)o, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        public static string Str(object o, string fallback = "") => o == null ? fallback : (o is string ? (string)o : Convert.ToString(o, CultureInfo.InvariantCulture));

        public static bool? Bool(object o) => o is bool ? (bool?)(bool)o : null;

        // Pretty output, two-space indent, like the files that ship with Capri.
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        public static void Save(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, Write(value), new UTF8Encoding(false)); }

        static void WriteValue(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is DateTime) { WriteString(sb, ((DateTime)v).ToString("o")); return; }
            if (IsNumber(v))
            {
                double d = Num(v);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("0"); return; }
                sb.Append(d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\r\n");
                int i = 0;
                foreach (DictionaryEntry kv in dict)
                {
                    sb.Append(' ', indent + 2); WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture)); sb.Append(": ");
                    WriteValue(sb, kv.Value, indent + 2);
                    if (++i < dict.Count) sb.Append(',');
                    sb.Append("\r\n");
                }
                sb.Append(' ', indent); sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                var items = new List<object>(); foreach (var x in list) items.Add(x);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\r\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(' ', indent + 2); WriteValue(sb, items[i], indent + 2);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append("\r\n");
                }
                sb.Append(' ', indent); sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
