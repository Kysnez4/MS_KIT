using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ModKit
{
    /// <summary>
    /// Журнал вылетов и защита от ошибок модов.
    /// Нативный сбой игры (SIGSEGV, Scudo, abort) перехватить из C# нельзя: процесс убивает система. Поэтому ядро:
    ///  1) помечает, какой мод и какой хук выполняется (<see cref="Enter"/>/<see cref="Leave"/>, ctx.Mark), и раз в 0,3 с пишет последние метки в lastrun.txt;
    ///  2) при следующем старте, если прошлый запуск не закончился штатно, пишет в modkit.log отчёт «игра упала, последним работал мод X в хуке Y»
    ///     и отключает мод, если сбой пришёлся прямо на его код;
    ///  3) считает ошибки C# по модам: серия ошибок подряд отключает мод, игра при этом продолжает работать.
    /// </summary>
    public static class Crash
    {
        private struct Rec { public string Owner, Tag; public bool Inside; public double At; }

        private const int Keep = 12;
        private static readonly List<Rec> Ring = new List<Rec>();
        private static readonly object Gate = new object();
        private static Rec _cur;
        private static int _version, _written;
        private static Timer _timer;
        private static string RunFile => Path.Combine(Paths.Dir, "running.lock");
        private static string LastFile => Path.Combine(Paths.Dir, "lastrun.txt");

        /// <summary>Старт журнала: разбор прошлого запуска, затем метка «идёт запуск».</summary>
        internal static CrashReport Begin()
        {
            CrashReport rep = null;
            try
            {
                if (File.Exists(RunFile))
                {
                    var lines = File.Exists(LastFile) ? File.ReadAllLines(LastFile) : new string[0];
                    rep = Parse(lines);
                }
                File.WriteAllText(RunFile, DateTime.Now.ToString("s"));
                File.WriteAllText(LastFile, "");
            }
            catch (Exception e) { Log.Write("журнал вылетов: " + e.Message); }
            _timer = new Timer(_ => Flush(), null, 300, 300);
            return rep;
        }

        /// <summary>Штатное завершение игры: метка запуска снимается, следующий старт не считается аварийным.</summary>
        internal static void End()
        {
            try { _timer?.Dispose(); if (File.Exists(RunFile)) File.Delete(RunFile); } catch { /* */ }
        }

        /// <summary>Мод (owner) начал выполнять код в точке tag. Дёшево: только запись в память.</summary>
        public static void Enter(string owner, string tag)
        {
            if (owner == "effects" || owner == "events") return;       // служебные хуки ядра: метки ставят вызываемые ими моды
            _cur = new Rec { Owner = owner, Tag = tag, Inside = true, At = Scheduler.Now };
            _version++;
        }

        /// <summary>Мод закончил выполнять код; дальше работает код игры.</summary>
        public static void Leave()
        {
            var c = _cur;
            if (!c.Inside) return;
            lock (Gate) { Ring.Add(c); if (Ring.Count > Keep) Ring.RemoveAt(0); }
            _cur = new Rec { Owner = c.Owner, Tag = c.Tag, Inside = false, At = Scheduler.Now };
            _version++;
        }

        private static void Flush()
        {
            try
            {
                if (_version == _written) return;
                _written = _version;
                List<Rec> ring;
                lock (Gate) ring = Ring.ToList();
                var c = _cur;
                var sb = new System.Text.StringBuilder();
                sb.Append(c.Inside ? "inside" : "after").Append('\t').Append(c.Owner ?? "").Append('\t').Append(c.Tag ?? "").Append('\t').Append(c.At.ToString("0.00")).Append('\t').Append(Scheduler.Now.ToString("0.00")).Append('\n');
                foreach (var r in ring.AsEnumerable().Reverse()) sb.Append("ring\t").Append(r.Owner).Append('\t').Append(r.Tag).Append('\t').Append(r.At.ToString("0.00")).Append('\n');
                File.WriteAllText(LastFile, sb.ToString());
            }
            catch { /* журнал не критичен */ }
        }

        private static CrashReport Parse(string[] lines)
        {
            var rep = new CrashReport();
            foreach (var l in lines)
            {
                var p = l.Split('\t');
                if (p.Length >= 5 && (p[0] == "inside" || p[0] == "after"))
                {
                    rep.Inside = p[0] == "inside"; rep.Owner = p[1]; rep.Tag = p[2];
                    double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at);
                    double.TryParse(p[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var now);
                    rep.AgeSec = now - at;
                }
                else if (p.Length >= 4 && p[0] == "ring") rep.Recent.Add($"{p[1]} · {p[2]}");
            }
            return rep;
        }

        // ---------------------------------------------------------------- ошибки C# по модам

        private class Stat { public int Count; public double WindowStart; }
        private static readonly Dictionary<string, Stat> Errors = new Dictionary<string, Stat>();
        /// <summary>Сколько ошибок за окно отключают мод.</summary>
        public const int MaxErrors = 5;
        /// <summary>Сколько ошибок модов записано за запуск (для автотестов).</summary>
        public static int TotalErrors;
        public const double WindowSec = 10;

        /// <summary>Записывает ошибку мода в журнал. Возвращает true, если мод превысил лимит и будет отключён.</summary>
        public static bool Error(string modId, string where, Exception e)
        {
            TotalErrors++;
            double now = Scheduler.Now;
            if (!Errors.TryGetValue(modId, out var s) || now - s.WindowStart > WindowSec) Errors[modId] = s = new Stat { WindowStart = now };
            s.Count++;
            var msg = e is AggregateException || e.InnerException == null ? e.ToString() : e.InnerException.ToString();
            Log.Write($"ОШИБКА [{modId}] {where} ({s.Count}/{MaxErrors}): {msg}");
            if (s.Count < MaxErrors) return false;
            Errors.Remove(modId);
            Core.DisableForErrors(modId, $"{MaxErrors} ошибок за {WindowSec:0} с (последняя: {where}: {e.Message})");
            return true;
        }
    }

    /// <summary>Что известно о прошлом аварийном запуске.</summary>
    public class CrashReport
    {
        public bool Inside;
        public string Owner, Tag;
        public double AgeSec;
        public List<string> Recent = new List<string>();
    }
}
