using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Il2Cpp;
using ModKit;
using UnityEngine;

namespace ProgressSaves
{
    /// <summary>
    /// Сохранение и загрузка прогресса. Сохранение игры — набор шифрованных *.json в Application.persistentDataPath
    /// (Android/data/пакет/files); файлы сверяются друг с другом, поэтому копируется и возвращается весь набор целиком
    /// (как во Frida-модах import_progress/progress_backup). Копии — в Download/ModKit/saves/пакет/время,
    /// автокопия после боя — в папку auto (предыдущая — auto_prev). Перед загрузкой текущие файлы уходят в копию «до загрузки»,
    /// после записи процесс убивается (kill -9): иначе игра перезапишет файлы из памяти при выходе.
    /// </summary>
    public class ProgressSavesMod : IKitMod
    {
        private static string Root => $"/sdcard/Download/ModKit/saves/{Pkg}";

        /// <summary>Пакет игры из пути MelonLoader (/sdcard/MelonLoader/пакет/UserData/ModKit): Application.identifier в игре вырезан (ICall не найден).</summary>
        private static string Pkg => _pkg ??= Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Paths.Dir)));
        private static string _pkg, _gameDir;
        private const int MaxManual = 10;      // ручных копий хранится не больше — самые старые удаляются
        private const double AutoDelay = 8;    // секунд после боя: игра уже дописала автосохранение

        [DllImport("libc")] private static extern int getpid();
        [DllImport("libc")] private static extern int kill(int pid, int sig);

        public ModDef Define() => new ModDef
        {
            Id = "progress_saves",
            Name = "Сохранение прогресса",
            Description = "Сохраняет прогресс игры в копию и загружает его обратно (кнопка «Настройки»). После загрузки игра закрывается.",
            Version = "1",
            OnEnable = ctx =>
            {
                ctx.Log($"папка сохранений игры: {GameDir}; копии: {Root}; сейчас: {(HasSave(GameDir) ? Describe(GameDir) : "сохранения нет")}");
                // самопроверка (скрытая настройка selftest): 1 — сохранить; 2 — сохранить и загрузить эту же копию (прогресс не меняется, игра закроется)
                int test = (int)ctx.Setting("selftest");
                if (test > 0)
                {
                    ctx.SetSetting("selftest", 0);
                    ctx.Guard("самопроверка", () =>
                    {
                        ctx.Log("самопроверка: " + Save(ctx).Replace('\n', ' '));
                        var last = Directory.GetDirectories(Root).Where(d => char.IsDigit(Path.GetFileName(d)[0])).OrderBy(d => d).Last();
                        ctx.Log($"самопроверка: набор совпадает с игрой: {SameSet(GameDir, last)}");
                        if (test == 2) Load(ctx, last);
                    });
                }
                DateTime? autoAt = null;
                ctx.Events.On("battleEnd", e => autoAt = DateTime.Now.AddSeconds(AutoDelay));
                ctx.Every(2000, () =>
                {
                    if (autoAt == null || DateTime.Now < autoAt) return;
                    autoAt = null;
                    ctx.Guard("автокопия", () => Auto(ctx));
                });
            },
            Options = { new ModOption { Key = "selftest", Label = "Самопроверка", Default = 0, Min = 0, Max = 2, Step = 1, Hidden = true } },
            OpenSettings = ctx => ctx.Guard("окно", () => Open(ctx)),
        };

        // ------------------------------------------------------------ файлы

        /// <summary>Папка сохранений игры: persistentDataPath, а если и он вырезан — стандартный путь Android/data/пакет/files.</summary>
        private static string GameDir
        {
            get
            {
                if (_gameDir != null) return _gameDir;
                try { _gameDir = Application.persistentDataPath; } catch { }
                if (string.IsNullOrEmpty(_gameDir)) _gameDir = $"/storage/emulated/0/Android/data/{Pkg}/files";
                return _gameDir;
            }
        }
        private static bool IsSave(string path) { var n = Path.GetFileName(path); return n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("_"); }
        private static List<string> SaveFiles(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir).Where(IsSave).OrderBy(f => f).ToList() : new List<string>();
        private static bool HasSave(string dir) => File.Exists(Path.Combine(dir, "List_SingleValue.json"));

        /// <summary>Копирует весь набор: сначала всё читается в память, потом пишется (при ошибке чтения папка не тронута).</summary>
        private static int CopySet(string from, string to)
        {
            var data = SaveFiles(from).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
            if (data.Count == 0) throw new Exception("в папке нет сохранений");
            Directory.CreateDirectory(to);
            foreach (var old in SaveFiles(to)) File.Delete(old);
            foreach (var kv in data)
            {
                var p = Path.Combine(to, kv.Key);
                File.WriteAllBytes(p, kv.Value);
                if (!File.ReadAllBytes(p).SequenceEqual(kv.Value)) throw new Exception($"не записался {kv.Key}");
            }
            return data.Count;
        }

        private static bool SameSet(string a, string b)
        {
            var fa = SaveFiles(a); var fb = SaveFiles(b);
            if (fa.Count == 0 || fa.Count != fb.Count) return false;
            for (int i = 0; i < fa.Count; i++)
                if (Path.GetFileName(fa[i]) != Path.GetFileName(fb[i]) || !File.ReadAllBytes(fa[i]).SequenceEqual(File.ReadAllBytes(fb[i]))) return false;
            return true;
        }

        // ------------------------------------------------------------ данные модов

        /// <summary>
        /// Данные модов из UserData/ModKit: *.json в корне (config.json — какие моды включены и их настройки, achievements.json и т.п.)
        /// и папка store (ctx.Store). Журналы, кэш, zip модов и служебные файлы не копируются. В копии лежат в подпапке modkit.
        /// </summary>
        private static List<(string rel, string full)> ModFiles(string root)
        {
            var res = new List<(string, string)>();
            if (!Directory.Exists(root)) return res;
            foreach (var f in Directory.GetFiles(root, "*.json")) res.Add((Path.GetFileName(f), f));
            var store = Path.Combine(root, "store");
            if (Directory.Exists(store))
                foreach (var f in Directory.GetFiles(store, "*.json")) res.Add(("store/" + Path.GetFileName(f), f));
            return res;
        }

        /// <summary>Копирует данные модов from → to (to очищается от прежних файлов модов); число файлов.</summary>
        private static int CopyMods(string from, string to)
        {
            var data = ModFiles(from).ToDictionary(x => x.rel, x => File.ReadAllBytes(x.full));
            if (data.Count == 0) return 0;
            foreach (var old in ModFiles(to)) File.Delete(old.full);
            foreach (var kv in data)
            {
                var p = Path.Combine(to, kv.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, kv.Value);
            }
            return data.Count;
        }

        private static string ModsOf(string copy) => Path.Combine(copy, "modkit");

        private static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        /// <summary>Золото, алмазы, уровень исследований из List_SingleValue (шифр игры Z_AESCrypto); не читается — так и пишем.</summary>
        private static string Describe(string dir)
        {
            try
            {
                var raw = File.ReadAllText(Path.Combine(dir, "List_SingleValue.json")).Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(Z_AESCrypto.AESDecrypt128(raw));
                var a = doc.RootElement;
                return $"золото {a[5]}, алмазы {a[9]}, исследования {a[7]}";
            }
            catch { return "прогресс не читается"; }
        }

        private static string Title(string dir)
        {
            var n = Path.GetFileName(dir);
            if (n == "auto") return "Автокопия после боя";
            if (n == "auto_prev") return "Автокопия, предыдущая";
            bool before = n.StartsWith("before_");
            var s = before ? n.Substring(7) : n;
            var when = DateTime.TryParseExact(s, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var d) ? d.ToString("dd.MM HH:mm") : s;
            return (before ? "До загрузки " : "Сохранение ") + when;
        }

        // ------------------------------------------------------------ действия

        private static void Auto(ModContext ctx)
        {
            if (!HasSave(GameDir)) return;
            string auto = Path.Combine(Root, "auto"), prev = Path.Combine(Root, "auto_prev");
            if (SameSet(GameDir, auto)) return;
            if (HasSave(auto)) { CopySet(auto, prev); CopyMods(ModsOf(auto), ModsOf(prev)); }
            ctx.Log($"автокопия после боя: файлов {CopySet(GameDir, auto)}, данных модов {CopyMods(Paths.Dir, ModsOf(auto))} → {auto}");
        }

        private static string Save(ModContext ctx)
        {
            if (!HasSave(GameDir)) return "Сохранение игры не найдено.";
            var dir = Path.Combine(Root, Stamp());
            int n = CopySet(GameDir, dir), m = CopyMods(Paths.Dir, ModsOf(dir));
            // ручные копии сверх лимита — удаляем самые старые
            var manual = Directory.GetDirectories(Root).Where(d => char.IsDigit(Path.GetFileName(d)[0])).OrderBy(d => d).ToList();
            foreach (var old in manual.Take(Math.Max(0, manual.Count - MaxManual))) Directory.Delete(old, true);
            ctx.Log($"сохранено: файлов {n}, данных модов {m} → {dir}");
            return $"Сохранено: {Describe(dir)}.\nПапка: Download/ModKit/saves/{Pkg}/{Path.GetFileName(dir)}";
        }

        private static void Load(ModContext ctx, string dir)
        {
            var before = Path.Combine(Root, "before_" + Stamp());
            if (HasSave(GameDir)) { CopySet(GameDir, before); CopyMods(Paths.Dir, ModsOf(before)); }
            int n = CopySet(dir, GameDir);
            // данные модов — только если они есть в копии (старые копии без них не трогают текущие настройки модов)
            int m = ModFiles(ModsOf(dir)).Count > 0 ? CopyMods(ModsOf(dir), Paths.Dir) : 0;
            ctx.Log($"загружено из {dir}: файлов {n}, данных модов {m}; закрываю игру, чтобы она не перезаписала файлы");
            // штатный выход для ядра: без метки running.lock следующий запуск не считается аварийным и моды не сбрасываются
            try { File.Delete(Path.Combine(Paths.Dir, "running.lock")); } catch { }
            kill(getpid(), 9);
        }

        // ------------------------------------------------------------ окно

        private static GameObject _win;

        private static void Open(ModContext ctx)
        {
            if (_win != null) UiKit.SafeDestroy(_win);
            var win = UiKit.Node("ModKitProgressSaves", UiKit.CanvasTf);
            _win = win.go;
            UiKit.Stretch(win.rt);
            UiKit.AddImage(win.go, 0.02, 0.03, 0.06, 1);
            UiKit.Tint(UiKit.Label(win.rt, "Сохранение прогресса", 0, 90, UiKit.LogicalW, 80, 46, 4), UiKit.Gold);
            UiKit.AddButton(win.rt, "X", UiKit.LogicalW - 100, 60, 80, 80, 54, new double[] { 0, 0, 0, 0.01 }, () => win.go.SetActive(false));
            var body = UiKit.Node("body", win.rt);
            UiKit.Place(body.rt, 0, 190, UiKit.LogicalW, UiKit.ScreenH - 190);

            double W = UiKit.LogicalW - 60;
            double[] green = { 0.20, 0.38, 0.22, 1 }, red = { 0.38, 0.20, 0.20, 1 }, blue = { 0.18, 0.28, 0.42, 1 }, gray = { 0.22, 0.22, 0.22, 1 };
            void Text(double y, string s, double h, double size = 23, Color? c = null) => UiKit.Tint(UiKit.Label(body.rt, s, 30, y, W, h, size, 0), c ?? UiKit.Col(0.85, 0.88, 0.95));
            void Btn(double y, string s, double[] bg, Action fn) => UiKit.AddButton(body.rt, s, 30, y, W, 80, 28, bg, fn);
            void Show(Action view) { UiKit.ClearChildren(body.rt); try { view(); } catch (Exception e) { ctx.Log("ошибка окна: " + e); Text(0, "Ошибка: " + e.Message, 200); } }

            Action main = null;
            void Done(string msg) => Show(() => { Text(0, msg, 300, 26, UiKit.Gold); Btn(320, "Назад", gray, main); });

            void Confirm(string dir) => Show(() =>
            {
                Text(0, $"Загрузить «{Title(dir)}»?", 80, 28, UiKit.Gold);
                Text(90, $"Будет: {Describe(dir)}.", 80);
                Text(180, "Вместе с прогрессом вернутся настройки и данные модов (если они есть в копии). Текущее сохранится в копию «До загрузки». После загрузки игра закроется — запустите её снова.", 120, 23, UiKit.Col(0.95, 0.8, 0.6));
                Btn(320, "Загрузить и закрыть игру", green, () =>
                {
                    try { Load(ctx, dir); }
                    catch (Exception e) { ctx.Log("ошибка загрузки: " + e); Done("Не получилось: " + e.Message + ".\nКопия «До загрузки» есть в списке."); }
                });
                Btn(420, "Отмена", red, main);
            });

            main = () => Show(() =>
            {
                Text(0, "Сейчас в игре: " + (HasSave(GameDir) ? Describe(GameDir) : "сохранение не найдено"), 60, 22, UiKit.Col(0.9, 0.8, 0.5));
                Btn(70, "Сохранить прогресс", green, () =>
                {
                    string msg;
                    try { msg = Save(ctx); } catch (Exception e) { ctx.Log("ошибка сохранения: " + e); msg = "Не получилось: " + e.Message; }
                    Done(msg);
                });
                var list = Directory.Exists(Root)
                    ? Directory.GetDirectories(Root).Where(HasSave).OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d, "List_SingleValue.json"))).Take(6).ToList()
                    : new List<string>();
                if (list.Count == 0) { Text(180, "Сохранённых копий пока нет.", 60); return; }
                Text(180, "Загрузить:", 40, 24, UiKit.Gold);
                double y = 230;
                foreach (var d in list)
                {
                    int mods = ModFiles(ModsOf(d)).Count;
                    Text(y, Describe(d) + (mods > 0 ? $", моды: {mods} файлов" : ", без данных модов"), 36, 21, UiKit.Col(0.5, 1, 0.5));
                    var dir = d;
                    Btn(y + 40, Title(d), blue, () => Confirm(dir));
                    y += 135;
                }
            });
            main();
        }
    }
}
