using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopMateOnMaimai.Config
{
    /// <summary>
    /// 极简 TOML 读取器。只支持本 Mod 用得到的子集：
    /// [Section.Sub] / Key = "str" / Key = 123 / Key = 1.5 / Key = true / Key = ["a","b"] / # 注释
    /// 刻意不引入 Tomlyn / Tomlet，避免与 MelonLoader 自带版本或 AquaMai 冲突。
    /// </summary>
    internal sealed class TinyToml
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static TinyToml Load(string path)
        {
            var toml = new TinyToml();
            if (!File.Exists(path)) return toml;

            string section = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                if (!toml._sections.TryGetValue(section, out var bag))
                {
                    bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    toml._sections[section] = bag;
                }
                bag[key] = val;
            }
            return toml;
        }

        /// <summary>去掉行内注释，但保留引号内的 #。</summary>
        private static string StripComment(string line)
        {
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inStr = !inStr;
                else if (c == '#' && !inStr) return line.Substring(0, i);
            }
            return line;
        }

        private string Raw(string section, string key)
        {
            if (_sections.TryGetValue(section, out var bag) && bag.TryGetValue(key, out var v)) return v;
            return null;
        }

        public string GetString(string section, string key, string def)
        {
            string v = Raw(section, key);
            if (v == null) return def;
            v = v.Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            return v;
        }

        public bool GetBool(string section, string key, bool def)
        {
            string v = Raw(section, key);
            if (v == null) return def;
            v = v.Trim().Trim('"').ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "on") return true;
            if (v == "false" || v == "0" || v == "no" || v == "off") return false;
            return def;
        }

        public int GetInt(string section, string key, int def)
        {
            string v = Raw(section, key);
            if (v == null) return def;
            return int.TryParse(v.Trim().Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        }

        public float GetFloat(string section, string key, float def)
        {
            string v = Raw(section, key);
            if (v == null) return def;
            return float.TryParse(v.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : def;
        }

        public string[] GetStringArray(string section, string key, string[] def)
        {
            string v = Raw(section, key);
            if (string.IsNullOrEmpty(v)) return def;
            v = v.Trim();
            if (!v.StartsWith("[") || !v.EndsWith("]")) return def;
            v = v.Substring(1, v.Length - 2);
            var list = new List<string>();
            foreach (var part in v.Split(','))
            {
                string s = part.Trim().Trim('"');
                if (s.Length > 0) list.Add(s);
            }
            return list.Count == 0 ? def : list.ToArray();
        }

        public float[] GetFloatArray(string section, string key, float[] def)
        {
            string v = Raw(section, key);
            if (string.IsNullOrEmpty(v)) return def;
            v = v.Trim();
            if (!v.StartsWith("[") || !v.EndsWith("]")) return def;
            v = v.Substring(1, v.Length - 2);
            var list = new List<float>();
            foreach (var part in v.Split(','))
            {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) list.Add(f);
            }
            return list.Count == 0 ? def : list.ToArray();
        }
    }
}
