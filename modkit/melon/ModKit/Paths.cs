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

        public static void Write(string msg)
        {
            MelonLogger.Msg(msg);
            try
            {
                lock (Gate) File.AppendAllText(Paths.LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { /* файловый журнал не критичен: есть Latest.log */ }
        }
    }
}
