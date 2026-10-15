import { defineMod } from "../api";

/**
 * Отключает выбор ультимейтов: игра хранит доступные для выбора ульты в статическом списке
 * `V_Ability_Manager.List_ApplyUltimatePossible`; мод держит его пустым (и сбрасывает уже выбранные `Z_Database.List_ApplyUltimateON`
 * не трогает — только блокирует новый выбор). Не проверено на устройстве: смотрите строки «[no_ultimate]» в modkit.log.
 */
export default defineMod({
    id: "no_ultimate",
    name: "Без ультимейтов",
    description: "Ультимейты нельзя выбрать: список доступных ультимейтов всегда пуст.",
    onEnable(ctx) {
        let cleared = 0;
        const clear = () => {
            const f = ctx.cls("V_Ability_Manager").field("List_ApplyUltimatePossible");
            const list = f.value as Il2Cpp.Object | null;
            if (!list || list.isNull()) return;
            if ((list.method("get_Count").invoke() as number) > 0) {
                list.method("Clear").invoke();
                if (++cleared <= 5) ctx.log(`[no_ultimate] список ультимейтов очищен (#${cleared})`);
            }
        };
        ctx.every(100, clear);
        // сразу после того, как игра пересобрала список при открытии окон выбора
        for (const m of ["Start_Ability", "Button_MagicCom_Decision", "Button_MagicCom_IN", "Button_AbilityCh_Decision"]) {
            try { ctx.after("V_Ability_Manager", m, 0, () => clear()); } catch (e) { ctx.log(`[no_ultimate] хук ${m}: ${e}`); }
        }
    },
});
