using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ModKit
{
    public class Candidate
    {
        public string Path, File, Id, Name, Version, Author;
        public bool Installed;
        /// <summary>Установленная версия (если мод уже стоит).</summary>
        public string InstalledVersion;
        /// <summary>new — мод не установлен, update — архив новее, downgrade — старее, same — та же.</summary>
        public string Relation;
    }

    public class LoadResult
    {
        public List<string> Added = new List<string>();
        public List<string> Errors = new List<string>();
        public int Files;
    }

    /// <summary>
    /// Внешние моды: архивы .zip. При старте грузятся установленные (UserData/ModKit/mods); новые ищет кнопка «+» в Download/ModKit и Download
    /// (только архивы с mod.json). Внутри: mod.json (id, название, описание, иконка, скриншоты...) и DLL с классом IKitMod.
    /// </summary>
    public static class External
    {
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();
        private static bool _resolverOn;

        /// <summary>Запоминает загруженную из zip сборку: другие моды, ссылающиеся на неё по имени, получат её же (общие типы).</summary>
        private static void Register(Assembly a)
        {
            Loaded[a.GetName().Name] = a;
            if (_resolverOn) return;
            _resolverOn = true;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => Loaded.TryGetValue(new AssemblyName(e.Name).Name, out var r) ? r : null;
        }

        private static readonly Dictionary<string, long> NotMods = new Dictionary<string, long>();

        private static IEnumerable<string> ListZips(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return Array.Empty<string>();
                return Directory.GetFiles(dir, "*.zip").Where(f => !System.IO.Path.GetFileName(f).StartsWith("_")).OrderBy(f => f, StringComparer.Ordinal).ToList();
            }
            catch (Exception e) { Log.Write($"каталог {dir}: {e.Message}"); return Array.Empty<string>(); }
        }

        /// <summary>Подгружает установленные архивы из папки модов.</summary>
        public static LoadResult LoadInstalled()
        {
            var res = new LoadResult();
            // библиотеки грузим первыми, а внутри — зависимости раньше зависимых: сборки ссылаются друг на друга по имени
            foreach (var path in LoadOrder(ListZips(Paths.ModsDir)))
            {
                res.Files++;
                var file = System.IO.Path.GetFileName(path);
                try
                {
                    var def = ReadModZip(path);
                    if (Core.AddExternal(def)) { res.Added.Add(def.Name); Log.Write($"мод загружен: {def.Id} ({path})"); }
                }
                catch (Exception e)
                {
                    res.Errors.Add($"{file}: {e.Message}");
                    Log.Write($"мод {file}: ошибка {e}");
                }
            }
            return res;
        }

        /// <summary>Порядок загрузки архивов: библиотеки первыми, зависимости (depends из mod.json) раньше тех, кто от них зависит.</summary>
        private static List<string> LoadOrder(IEnumerable<string> zips)
        {
            var info = new List<(string Path, string Id, bool Lib, string[] Deps)>();
            foreach (var path in zips)
            {
                string id = null; bool lib = false; var deps = new string[0];
                try
                {
                    using var zip = ZipFile.OpenRead(path);
                    var e = zip.GetEntry("mod.json");
                    if (e != null)
                    {
                        using var doc = JsonDocument.Parse(ReadAll(e), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                        id = Str(doc.RootElement, "id");
                        lib = Bool(doc.RootElement, "library", false);
                        if (doc.RootElement.TryGetProperty("depends", out var dep) && dep.ValueKind == JsonValueKind.Object) deps = dep.EnumerateObject().Select(x => x.Name).ToArray();
                    }
                }
                catch { /* битый архив: ошибку покажет загрузка */ }
                info.Add((path, id, lib, deps));
            }
            var byId = new Dictionary<string, (string Path, string Id, bool Lib, string[] Deps)>();
            foreach (var i in info) if (i.Id != null && !byId.ContainsKey(i.Id)) byId[i.Id] = i;
            var order = new List<string>();
            var seen = new HashSet<string>();
            void Visit((string Path, string Id, bool Lib, string[] Deps) item)
            {
                if (!seen.Add(item.Path)) return;
                foreach (var d in item.Deps) if (byId.TryGetValue(d, out var di)) Visit(di);
                order.Add(item.Path);
            }
            foreach (var i in info.OrderBy(x => x.Lib ? 0 : 1).ThenBy(x => x.Path, StringComparer.Ordinal)) Visit(i);
            return order;
        }

        private static string RelationOf(string candidate, string installed)
        {
            int c = Core.CompareVersions(candidate ?? "0", installed ?? "0");
            return c > 0 ? "update" : c < 0 ? "downgrade" : "same";
        }

        /// <summary>Ищет в папках загрузок .zip с mod.json (код мода не загружается).</summary>
        public static List<Candidate> FindCandidates()
        {
            var output = new List<Candidate>();
            var seen = new HashSet<string>();
            foreach (var dir in Paths.SearchDirs)
            {
                foreach (var path in ListZips(dir))
                {
                    if (!seen.Add(path)) continue;
                    long size;
                    try { size = new FileInfo(path).Length; } catch { continue; }
                    if (NotMods.TryGetValue(path, out var known) && known == size) continue;
                    try
                    {
                        using var zip = ZipFile.OpenRead(path);
                        var entry = zip.GetEntry("mod.json");
                        if (entry == null) { NotMods[path] = size; continue; }       // обычный zip без mod.json
                        using var doc = JsonDocument.Parse(ReadAll(entry), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                        var root = doc.RootElement;
                        var id = Str(root, "id");
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!(Str(root, "main") ?? "main.dll").EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;   // старые JS-моды (Frida) не подходят
                        var cur = Core.Mods.FirstOrDefault(m => m.Def.Id == id);
                        var version = Str(root, "version");
                        output.Add(new Candidate
                        {
                            Path = path, File = System.IO.Path.GetFileName(path), Id = id, Name = Str(root, "name") ?? id,
                            Version = version, Author = Str(root, "author"),
                            Installed = cur != null,
                            InstalledVersion = cur != null ? Core.ModVersion(cur) : null,
                            Relation = cur == null ? "new" : RelationOf(version, Core.ModVersion(cur)),
                        });
                    }
                    catch { /* не мод или битый архив */ }
                }
            }
            return output;
        }

        private static bool StoreZip(Candidate c, string dest)
        {
            try
            {
                Directory.CreateDirectory(Paths.ModsDir);
                File.Copy(c.Path, dest, true);
                return true;
            }
            catch (Exception e) { Log.Write($"копия {c.File} в mods не удалась: {e.Message} (мод работает до перезапуска)"); return false; }
        }

        /// <summary>Подключает мод из найденного архива (или обновляет установленный). Возвращает сообщение; бросает исключение с причиной.</summary>
        public static string InstallCandidate(Candidate c)
        {
            var def = ReadModZip(c.Path);
            var cur = Core.Mods.FirstOrDefault(m => m.Def.Id == def.Id);
            if (cur != null)
            {
                if (c.Relation == "same") throw new Exception("эта версия уже установлена");
                var from = Core.ModVersion(cur);
                var dest = cur.Def.Source ?? System.IO.Path.Combine(Paths.ModsDir, c.File);
                Core.ReplaceMod(cur, def);               // при ошибке бросает исключение, прежняя версия остаётся
                def.Source = StoreZip(c, dest) ? dest : c.Path;
                var down = c.Relation == "downgrade";
                Log.Write($"мод {(down ? "откачен" : "обновлён")}: {def.Id} {from ?? "?"} -> {Core.ModVersion(cur) ?? "?"} ({c.Path})");
                return $"{(down ? "Откат" : "Обновлён")}: {def.Name} {(from != null ? "v" + from : "")} → v{Core.ModVersion(cur) ?? "?"}";
            }
            if (!Core.AddExternal(def)) throw new Exception("мод с таким id уже установлен");
            var target = System.IO.Path.Combine(Paths.ModsDir, c.File);
            def.Source = StoreZip(c, target) ? target : c.Path;
            Log.Write($"мод установлен: {def.Id} ({c.Path})");
            return $"Установлен: {def.Name}";
        }

        // ---------------------------------------------------------------- чтение архива

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        private static string Str(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

        private static bool Bool(JsonElement o, string name, bool def) =>
            o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;

        private static double Num(JsonElement o, string name, double def) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

        private static List<string> StrList(JsonElement o, string name)
        {
            var l = new List<string>();
            if (o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) l.Add(x.GetString());
            return l;
        }

        /// <summary>Разбирает архив мода: mod.json + DLL + картинки. Бросает исключение с понятным текстом.</summary>
        public static ModDef ReadModZip(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var metaEntry = zip.GetEntry("mod.json") ?? throw new Exception("нет файла mod.json");
            using var doc = JsonDocument.Parse(ReadAll(metaEntry), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var meta = doc.RootElement;
            var mainName = Str(meta, "main") ?? "main.dll";
            if (!mainName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new Exception($"{mainName}: JS-моды здесь не поддерживаются, нужна C#-библиотека (.dll)");
            var mainEntry = zip.GetEntry(mainName) ?? throw new Exception($"нет библиотеки {mainName}");

            // дополнительные DLL (API библиотеки) грузим первыми и регистрируем для разрешения ссылок по имени сборки
            foreach (var extra in StrList(meta, "assemblies"))
            {
                var ee = zip.GetEntry(extra) ?? throw new Exception($"нет библиотеки {extra}");
                Register(Assembly.Load(ReadAll(ee)));
            }
            var asm = Assembly.Load(ReadAll(mainEntry));
            Register(asm);
            Type[] all;
            try { all = asm.GetTypes(); }
            catch (Exception e) when (e is ReflectionTypeLoadException || e is FileNotFoundException || e is FileLoadException)
            {
                var needs = meta.TryGetProperty("depends", out var dp) && dp.ValueKind == JsonValueKind.Object ? string.Join(", ", dp.EnumerateObject().Select(p => p.Name)) : "";
                throw new Exception("не загрузилась библиотека, от которой зависит мод" + (needs != "" ? $" (нужны: {needs})" : "") + ". Сначала установите её");
            }
            var types = all.Where(t => typeof(IKitMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToList();
            if (types.Count != 1) throw new Exception($"{mainName}: нужен ровно один класс с интерфейсом IKitMod (найдено {types.Count})");
            var def = ((IKitMod)Activator.CreateInstance(types[0])).Define() ?? throw new Exception("Define() вернул null");
            def.Library = Bool(meta, "library", def.Library);
            if (def.OnEnable == null && !def.Library) throw new Exception("в ModDef нет OnEnable");

            // mod.json главнее данных в коде
            def.Id = Str(meta, "id") ?? def.Id;
            def.Name = Str(meta, "name") ?? def.Name;
            def.Description = Str(meta, "description") ?? def.Description;
            if (Str(meta, "version") != null) def.Version = Str(meta, "version");
            if (meta.TryGetProperty("depends", out var dep) && dep.ValueKind == JsonValueKind.Object)
                def.Depends = dep.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString());
            if (meta.TryGetProperty("loadAfter", out _)) def.LoadAfter = StrList(meta, "loadAfter");
            if (meta.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                def.Options = opts.EnumerateArray().Select(o => new ModOption
                {
                    Key = Str(o, "key"), Label = Str(o, "label") ?? Str(o, "key"),
                    Default = Num(o, "default", 0), Min = Num(o, "min", 0), Max = Num(o, "max", 100), Step = Num(o, "step", 1),
                    Hidden = Bool(o, "hidden", false),
                }).ToList();
            }
            def.RestartRequired = Bool(meta, "restartRequired", def.RestartRequired);
            def.DefaultEnabled = Bool(meta, "defaultEnabled", def.DefaultEnabled);
            if (string.IsNullOrEmpty(def.Id) || !System.Text.RegularExpressions.Regex.IsMatch(def.Id, @"^[\w.-]+$"))
                throw new Exception("в mod.json нужен id (латиница, цифры, _ . -)");
            if (string.IsNullOrEmpty(def.Name)) throw new Exception("в mod.json нужен name");

            def.Source = path;
            byte[] Image(string n) { var e = string.IsNullOrEmpty(n) ? null : zip.GetEntry(n); return e == null ? null : ReadAll(e); }
            def.Meta = new ModMeta
            {
                Version = Str(meta, "version"),
                Author = Str(meta, "author"),
                Icon = Image(Str(meta, "icon") ?? "icon.png"),
                Screenshots = StrList(meta, "screenshots").Select(Image).Where(b => b != null).ToList(),
            };
            return def;
        }
    }
}
