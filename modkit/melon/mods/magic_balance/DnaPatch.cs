using System;
using System.Runtime.InteropServices;
using ModKit;

namespace MagicBalance
{
    /// <summary>
    /// ДНК без снижения эффективности. Игра (C_Battle_AllyUnit.StartInformation_B) при AllyUnitVar_EvolutionValueNum > 0
    /// считает бонус циклом по обелискам: s9 += pow(0.85, i) × 8. Множитель 0.85 грузится инструкцией `ldr sN, [xM, #0xac]`
    /// прямо перед `fmov sK, #8.0`; меняем её на `fmov sN, #1.0` — каждый обелиск даёт ровно 8. Константу 0.85 не трогаем:
    /// её могут читать и другие места. Адрес ищется по шаблону «ldr [#0xabc] (EvolutionValueNum) → ldr [#0xac] → fmov #8.0»,
    /// в версии 612 — RVA 0x11c15c8. Не нашлось — ничего не пишем. При выключении инструкция возвращается.
    /// </summary>
    internal static class DnaPatch
    {
        private const uint LdrSMask = 0xFFC00000, LdrS = 0xBD400000;     // LDR St, [Xn, #imm] (imm12 × 4)
        private const uint FmovMask = 0xFFFFFFE0, Fmov8 = 0x1E241000, Fmov1 = 0x1E2E1000;
        private const int ScanBytes = 0x20000;

        [DllImport("libc", SetLastError = true)]
        private static extern int mprotect(IntPtr addr, UIntPtr len, int prot);

        private static bool IsLdrS(uint ins, int offset) => (ins & LdrSMask) == LdrS && ((ins >> 10) & 0xFFF) == (uint)(offset / 4);

        public static void Enable(ModContext ctx)
        {
            IntPtr func, addr;
            try { (func, addr) = Find(); }
            catch (Exception e) { ctx.Warn("ДНК: не нашёл формулу игры, эффективность будет снижаться как в игре: " + e.Message); return; }
            uint orig = (uint)Marshal.ReadInt32(addr);
            uint patched = Fmov1 | (orig & 0x1F);                          // тот же регистр назначения
            if (!Write(addr, patched)) { ctx.Warn("ДНК: не удалось записать инструкцию (mprotect)"); return; }
            ctx.Log($"ДНК: снижение эффективности убрано (StartInformation_B + 0x{addr.ToInt64() - func.ToInt64():x})");
            ctx.OnCleanup(() => { if ((uint)Marshal.ReadInt32(addr) == patched) Write(addr, orig); });
        }

        private static (IntPtr, IntPtr) Find()
        {
            var method = Raw.Method(Raw.Class("Assembly-CSharp.dll", "", "C_Battle_AllyUnit"), "StartInformation_B", 0);
            var code = Marshal.ReadIntPtr(method);                       // MethodInfo.methodPointer
            if (code == IntPtr.Zero) throw new Exception("нет адреса функции");
            int lastEvo = -1;
            for (int off = 0; off < ScanBytes; off += 4)
            {
                uint ins = (uint)Marshal.ReadInt32(code + off);
                if (IsLdrS(ins, 0xabc)) lastEvo = off;
                if ((ins & FmovMask) != Fmov8 || lastEvo < 0 || off - lastEvo > 40 * 4) continue;
                for (int k = 4; k <= 4 * 4; k += 4)
                    if (IsLdrS((uint)Marshal.ReadInt32(code + off - k), 0xac)) return (code, code + off - k);
            }
            throw new Exception("шаблон не найден (другая версия игры?)");
        }

        /// <summary>Записывает 4 байта кода: страница на время записи RWX, потом снова R-X (как в legend_guard).</summary>
        private static bool Write(IntPtr addr, uint value)
        {
            long page = Environment.SystemPageSize;
            var start = new IntPtr(addr.ToInt64() & ~(page - 1));
            var len = new UIntPtr((ulong)page);
            if (mprotect(start, len, 7) != 0) return false;
            Marshal.WriteInt32(addr, unchecked((int)value));
            mprotect(start, len, 5);
            return (uint)Marshal.ReadInt32(addr) == value;
        }
    }
}
