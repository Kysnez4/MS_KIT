using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace ModKit
{
    /// <summary>Каталоги и журнал загрузчика. Всё лежит в /sdcard/MelonLoader/&lt;пакет&gt;/UserData/ModKit.</summary>
    public static class Paths
    {
        public static readonly string Dir = Path.Combine(MelonEnvironment.UserDataDirectory, "ModKit");
        public static readonly string ModsDir = Path.Combine(Dir, "mods");
        public static readonly string ConfigPath = Path.Combine(Dir, "config.json");
        public static readonly string LogPath = Path.Combine(Dir, "modkit.log");
        /// <summary>Журнал прошлого запуска: при старте modkit.log переносится сюда, новый пишется с чистого файла.</summary>
        public static readonly string PrevLogPath = Path.Combine(Dir, "modkit.prev.log");
        /// <summary>Сколько журналов хранится вместе с текущим: modkit.log, modkit.prev.log, modkit.prev2.log … modkit.prev4.log.</summary>
        public const int KeepLogs = 5;

        /// <summary>Журнал n-го прошлого запуска (1 — modkit.prev.log, 2 — modkit.prev2.log...).</summary>
        public static string PrevLog(int n) => n <= 1 ? PrevLogPath : Path.Combine(Dir, $"modkit.prev{n}.log");
        /// <summary>Где кнопка «+» ищет архивы с mod.json (только по нажатию).</summary>
        public static readonly string[] SearchDirs = { "/sdcard/Download/ModKit", "/sdcard/Download" };

        public static void Init()
        {
            foreach (var d in new[] { Dir, ModsDir, SearchDirs[0] })
            {
                try { Directory.CreateDirectory(d); } catch (Exception e) { MelonLogger.Warning($"каталог {d}: {e.Message}"); }
            }
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();
        private static bool _rotated;

        public static void Write(string msg)
        {
            MelonLogger.Msg(msg);
            try
            {
                lock (Gate)
                {
                    if (!_rotated)
                    {
                        _rotated = true;      // первая запись за запуск: журналы сдвигаются (prev → prev2 ...), самый старый удаляется, новый с чистого файла
                        Rotate();
                    }
                    File.AppendAllText(Paths.LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                }
            }
            catch { /* файловый журнал не критичен: есть Latest.log */ }
        }

        private static void Rotate()
        {
            int last = Paths.KeepLogs - 1;
            try { if (File.Exists(Paths.PrevLog(last))) File.Delete(Paths.PrevLog(last)); } catch { /* */ }
            for (int n = last - 1; n >= 1; n--)
                try { if (File.Exists(Paths.PrevLog(n))) File.Move(Paths.PrevLog(n), Paths.PrevLog(n + 1), true); } catch { /* */ }
            try { if (File.Exists(Paths.LogPath)) File.Move(Paths.LogPath, Paths.PrevLog(1), true); } catch { /* */ }
        }
    }
}
