using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2Cpp;

namespace ModKit
{
    /// <summary>
    /// Переводы модов, как lang/ в ресурс-паках Minecraft: в zip мода лежат lang/ru.json, lang/en.json… — плоские
    /// объекты «ключ → строка». Язык берётся из настроек игры (Z_Database.Game_SystemLanguage), запасные варианты:
    /// язык игры → en → ru → значение по умолчанию. Ключи name и description переводят название и описание мода в окне «Моды».
    /// </summary>
    public static class Lang
    {
        /// <summary>Коды языков по номеру Game_SystemLanguage (кнопки выбора языка в A_MainMenuInformation.Button_Language_*).</summary>
        private static readonly string[] Codes = { null, "ko", "en", "ja", "zh_cn", "zh_tw", "es", "de", "pt", "ru" };

        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Cache =
            new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

        /// <summary>Код языка игры: ko, en, ja, zh_cn, zh_tw, es, de, pt, ru (до загрузки данных игры — en).</summary>
        public static string Code
        {
            get
            {
                try { int v = Z_Database.Game_SystemLanguage; if (v > 0 && v < Codes.Length) return Codes[v]; }
                catch { /* Z_Database ещё не готов */ }
                return "en";
            }
        }

        /// <summary>Перевод ключа из lang/*.json мода (zip по пути source); нет перевода — fallback (или сам ключ).</summary>
        public static string T(string source, string key, string fallback = null)
        {
            foreach (var code in new[] { Code, "en", "ru" })
            {
                var d = Table(source, code);
                if (d != null && d.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s)) return s;
            }
            return fallback ?? key;
        }

        private static Dictionary<string, string> Table(string source, string code)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!Cache.TryGetValue(source, out var langs)) Cache[source] = langs = new Dictionary<string, Dictionary<string, string>>();
            if (langs.TryGetValue(code, out var d)) return d;
            d = null;
            var bytes = Assets.Read(source, $"lang/{code}.json");
            if (bytes != null)
            {
                try
                {
                    d = new Dictionary<string, string>();
                    using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString();
                }
                catch (Exception e) { Log.Write($"lang/{code}.json в {System.IO.Path.GetFileName(source)}: {e.Message}"); d = null; }
            }
            langs[code] = d;
            return d;
        }
    }
}
