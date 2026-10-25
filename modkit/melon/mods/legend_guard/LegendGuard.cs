using System;
using System.Runtime.InteropServices;
using ModKit;

namespace LegendGuard
{
    /// <summary>
    /// Штраф игры за «слишком сильного» героя (C_Battle_AllyUnit.StartInformation_B): если у героя 15+ артефактов с грейдом 5
    /// (легендарные) и не включён C_Battle_Manager.IsTestON, игра делает EnemyUnitVar_HpMaxRatioU += 1 000 000 и при 21+
    /// артефактах трижды RemoveAt(0) у List_ArtifactObtain (подробно — docs/DIFFICULTY.md).
    /// Мод один раз меняет порог в инструкции `cmp wN, #15` на 4095 — проверка больше не срабатывает, хуков и работы во время
    /// боя нет. Адрес не зашит: начало функции берётся у IL2CPP (methodPointer), внутри ищется шаблон «cmp wN,#15 → чтение
    /// IsTestON (ldrb [#0x24c]) → cmp wM,#21» (в версии 612 — RVA 0x11c123c). Регистр в инструкции сохраняется, меняется
    /// только число. Шаблон не найден — ничего не пишем. При выключении инструкция возвращается. IsTestON не трогаем: тот же флаг в этой функции подменяет формулу характеристик героя.
    /// </summary>
    public class LegendGuardMod : IKitMod
    {
        // SUBS (imm, 32 бита) с Rd = wzr, без сдвига: cmp wN, #imm. Маска оставляет imm12, убирая регистр Rn
        private const uint CmpMask = 0xFFFFFC1F, Cmp15 = 0x71003C1F, Cmp21 = 0x7100541F, Cmp4095 = 0x713FFC1F;
        private const uint ImmMask = 0x003FFC00;
        private const int ScanBytes = 0x20000;      // функция огромная, порог в версии 612 — через 0xBB8 байт от начала

        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(IntPtr addr, UIntPtr len, int prot);

        private static IntPtr _func;               // начало StartInformation_B — для журнала

        private const int ProtRead = 1, ProtWrite = 2, ProtExec = 4;

        public ModDef Define() => new ModDef
        {
            Id = "legend_guard",
            Name = "Без штрафа за легендарные",
            Description = "Отключает штраф игры за 15+ легендарных: +1 000 000% здоровья врагам и удаление трёх старых артефактов.",
            Version = "1.0",
            DefaultEnabled = true,
            OnEnable = ctx =>
            {
                IntPtr addr;
                try { addr = FindThreshold(); }
                catch (Exception e) { ctx.Warn("не нашёл проверку легендарных: " + e.Message); return; }
                uint orig = (uint)Marshal.ReadInt32(addr);
                if ((orig & CmpMask) == Cmp4095) { ctx.Log("порог уже снят"); return; }
                uint patched = (orig & ~ImmMask) | (Cmp4095 & ImmMask);
                if (!Write(addr, patched)) { ctx.Warn("не удалось записать инструкцию (mprotect)"); return; }
                ctx.Log($"штраф за 15+ легендарных отключён (StartInformation_B + 0x{addr.ToInt64() - _func.ToInt64():x})");
                ctx.OnCleanup(() =>
                {
                    if ((uint)Marshal.ReadInt32(addr) == patched && Write(addr, orig)) ctx.Log("штраф за легендарные возвращён");
                });
            },
        };

        /// <summary>Ищет инструкцию порога в C_Battle_AllyUnit.StartInformation_B по шаблону (см. описание класса).</summary>
        private static IntPtr FindThreshold()
        {
            var klass = Raw.Class("Assembly-CSharp.dll", "", "C_Battle_AllyUnit");
            var method = Raw.Method(klass, "StartInformation_B", 0);
            var code = _func = Marshal.ReadIntPtr(method);          // MethodInfo.methodPointer
            if (code == IntPtr.Zero) throw new Exception("нет адреса функции");
            for (int off = 0; off < ScanBytes; off += 4)
            {
                uint ins = (uint)Marshal.ReadInt32(code + off);
                if ((ins & CmpMask) != Cmp15) continue;
                bool flag = false, list = false;
                for (int k = 4; k <= 40 * 4 && !list; k += 4)
                {
                    uint next = (uint)Marshal.ReadInt32(code + off + k);
                    if ((next & 0xFFFFFC00) == (0x39400000 | (0x24c << 10))) flag = true;   // ldrb wT, [xN, #0x24c]
                    if (flag && (next & CmpMask) == Cmp21) list = true;
                }
                if (list) return code + off;
            }
            throw new Exception("шаблон не найден (другая версия игры?)");
        }

        /// <summary>Записывает 4 байта кода: страница на время записи RWX, потом снова R-X.</summary>
        private static bool Write(IntPtr addr, uint value)
        {
            long page = Environment.SystemPageSize;
            var start = new IntPtr(addr.ToInt64() & ~(page - 1));
            var len = new UIntPtr((ulong)page);
            if (mprotect(start, len, ProtRead | ProtWrite | ProtExec) != 0) return false;
            Marshal.WriteInt32(addr, unchecked((int)value));
            mprotect(start, len, ProtRead | ProtExec);
            return (uint)Marshal.ReadInt32(addr) == value;
        }

    }
}
