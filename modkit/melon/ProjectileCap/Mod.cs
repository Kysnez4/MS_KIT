using HarmonyLib;
using Il2Cpp;
using MelonLoader;

[assembly: MelonInfo(typeof(ProjectileCap.Mod), "ProjectileCap", "0.1", "Pavel")]
[assembly: MelonGame(null, null)]

namespace ProjectileCap
{
    /// <summary>Пробный C#-мод: после пересчёта характеристик (AbilityStatApply) пишет в лог число спутников героя.</summary>
    public class Mod : MelonMod
    {
        public override void OnInitializeMelon() => LoggerInstance.Msg("ProjectileCap загружен");

        [HarmonyPatch(typeof(C_Battle_AllyUnit), nameof(C_Battle_AllyUnit.Unit_StatBuff))]
        private static class StatBuffPatch
        {
            private static float last = -1;
            private static void Postfix(C_Battle_AllyUnit __instance)
            {
                float n = __instance.UnitApply_SatelliteNum;
                if (n != last) { last = n; MelonLogger.Msg($"Unit_StatBuff: UnitApply_SatelliteNum = {n}"); }
            }
        }
    }
}
