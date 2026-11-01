using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace DesktopMateOnMaimai.Core
{
    /// <summary>
    /// 人物资源加载。
    /// 目录约定：Mods/DesktopMate/Characters/&lt;id&gt;/
    ///   character.ab     —— 用 Unity 2018.4.7f1 导出的 AssetBundle（内含 1 个角色预制体）
    ///   sprites/*.png    —— 2D 兜底序列帧
    ///   character.json   —— 可选元数据（显示名 / 表情层 / 缩放微调）
    ///
    /// 注意：DesktopMate 原版的 .cb 包是 Unity 6000 + 加密(DMCryptoStream)格式，
    /// 与 2018.4 运行时不兼容，必须重新导出，不能直接丢进来用。
    /// </summary>
    internal static class CharacterLoader
    {
        private static readonly Dictionary<string, AssetBundle> Cache = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        public static string FindBundle(string characterDir)
        {
            if (string.IsNullOrEmpty(characterDir) || !Directory.Exists(characterDir)) return null;

            string preferred = Path.Combine(characterDir, "character.ab");
            if (File.Exists(preferred)) return preferred;

            foreach (var pattern in new[] { "*.ab", "*.unity3d", "*.bundle" })
            {
                var files = Directory.GetFiles(characterDir, pattern);
                if (files.Length > 0) return files[0];
            }
            return null;
        }

        public static GameObject LoadPrefab(string bundlePath, string characterId)
        {
            try
            {
                if (!Cache.TryGetValue(bundlePath, out var bundle))
                {
                    bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (bundle == null)
                    {
                        MelonLogger.Warning("[DesktopMate] AssetBundle 打不开: " + bundlePath);
                        return null;
                    }
                    Cache[bundlePath] = bundle;
                }

                // 优先按名字取主预制体
                var names = bundle.GetAllAssetNames();
                foreach (var n in names)
                {
                    if (!n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    string shortName = Path.GetFileNameWithoutExtension(n);
                    if (!string.IsNullOrEmpty(characterId) &&
                        shortName.IndexOf(characterId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var go = bundle.LoadAsset<GameObject>(n);
                        if (go != null) return go;
                    }
                }

                foreach (var n in names)
                {
                    if (!n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = bundle.LoadAsset<GameObject>(n);
                    if (go != null) return go;
                }

                var all = bundle.LoadAllAssets<GameObject>();
                return all.Length > 0 ? all[0] : null;
            }
            catch (Exception e)
            {
                MelonLogger.Error("[DesktopMate] 加载角色资源包失败（多半是 Unity 版本不匹配，" +
                                  "请用 Unity 2018.4.7f1 重新导出）: " + e.Message);
                return null;
            }
        }

        /// <summary>可选：读取 character.json 里的简单元数据。</summary>
        public static Dictionary<string, string> LoadManifest(string characterDir)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string p = Path.Combine(characterDir, "character.json");
                if (!File.Exists(p)) return dict;

                // 手写极简 JSON（只认一层 key: value），避免引入 JSON 库
                string text = File.ReadAllText(p);
                foreach (var raw in text.Split(','))
                {
                    string line = raw.Trim().Trim('{', '}', '\r', '\n', ' ');
                    int c = line.IndexOf(':');
                    if (c <= 0) continue;
                    string k = line.Substring(0, c).Trim().Trim('"');
                    string v = line.Substring(c + 1).Trim().Trim('"');
                    if (k.Length > 0) dict[k] = v;
                }
            }
            catch { }
            return dict;
        }

        public static void UnloadAll()
        {
            foreach (var b in Cache.Values)
            {
                try { if (b != null) b.Unload(false); } catch { }
            }
            Cache.Clear();
        }
    }
}
