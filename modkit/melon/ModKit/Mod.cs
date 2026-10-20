using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(ModKit.Loader), "ModKit", "1.0", "Pavel")]
[assembly: MelonGame(null, null)]

namespace ModKit
{
    /// <summary>Загрузчик модов: реестр, окно «Моды», zip-моды. Встроенные моды регистрируются из <see cref="BuiltinMods"/>.</summary>
    public class Loader : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Paths.Init();
            Log.Write("ModKit запущен");
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                BattleState.Install();
                Core.Register(BuiltinMods.All());
                var ext = External.LoadInstalled();
                Log.Write($"внешних файлов: {ext.Files}, подключено модов: {ext.Added.Count}, ошибок: {ext.Errors.Count}");
                Core.Start(Crash.Begin());
            }
            catch (Exception e) { Log.Write("запуск загрузчика: " + e); }
        }

        public override void OnApplicationQuit() => Crash.End();

        public override void OnUpdate()
        {
            Scheduler.Tick();
            MenuUi.Tick();
        }

        [HarmonyPatch(typeof(A_MainMenuInformation), nameof(A_MainMenuInformation.Update))]
        private static class MainMenuUpdatePatch
        {
            private static void Postfix(A_MainMenuInformation __instance)
            {
                BattleState.MenuTick();
                try { MenuUi.OnMenuUpdate(__instance); } catch (Exception e) { Log.Write("меню: " + e); }
            }
        }

        [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerDown))]
        private static class PointerDownPatch
        {
            private static void Postfix(Selectable __instance) => MenuUi.OnPointerDown(__instance);
        }

        [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnPointerUp))]
        private static class PointerUpPatch
        {
            private static void Postfix() => MenuUi.OnPointerUp();
        }
    }

    /// <summary>Встроенные моды (пока пусто: переносятся с TypeScript по одному).</summary>
    public static class BuiltinMods
    {
        public static IEnumerable<ModDef> All() => new List<ModDef>();
    }
}
