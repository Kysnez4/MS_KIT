using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Il2Cpp;

namespace ModKit
{
    // ------------------------------------------------------------ Файлы мода и уведомления

    internal static class Assets
    {
        /// <summary>Читает файл из zip мода (путь внутри архива) или null.</summary>
        public static byte[] Read(string zipPath, string entry)
        {
            try
            {
                if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath)) return null;
                using (var z = ZipFile.OpenRead(zipPath))
                {
                    var e = z.GetEntry(entry.Replace('\\', '/'));
                    if (e == null) return null;
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
                }
            }
            catch (Exception ex) { Log.Write("ReadAsset: " + ex.Message); return null; }
        }
    }


    internal static class Toasts
    {
        private static int _seq;
        /// <summary>Подпись на экране на sec секунд (по игровому времени, на паузе не гаснет).</summary>
        public static void Show(ModContext c, string text, double sec)
        {
            var name = "toast" + (++_seq);
            var l = UiKit.AddHudLabel(name, text, 60, 180 + (_seq % 3) * 50, 600, 46, 28, new[] { 1.0, 0.92, 0.6, 1.0 });
            c.Timer.After(sec, () => l.Destroy());
            c.OnCleanup(l.Destroy);
        }
    }
}
