import { defineMod } from "../api";

/** Отладка: число молний — переменные Z_Database.AllyUnitVar_Thunder* и поля героя UnitBase_/UnitApply_ThunderNum. */
export default defineMod({
    id: "probe",
    name: "Зонд молний (отладка)",
    description: "Пишет в лог переменные, связанные с числом молний.",
    options: [],
    onEnable(ctx) {
        let calls = 0, last = "";
        ctx.after("C_Battle_Manager", "Update", 0, mgr => {
            if (++calls % 100 !== 0) return;
            const z = ctx.cls("Z_Database");
            const parts: string[] = [];
            for (const f of z.fields) {
                if (!f.isStatic || !f.name.startsWith("AllyUnitVar_Thunder")) continue;
                const v = f.value as any;
                if (typeof v === "number") parts.push(`${f.name.slice(12)}=${Math.round(v * 1000) / 1000}`);
            }
            // герой
            const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
            if (!field.isNull()) {
                const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
                const type = ctx.cls("C_Battle_AllyUnit").type.object;
                for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                    const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
                    const c = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                    if (c && !c.isNull()) {
                        parts.push(`hero.UnitBase_ThunderNum=${c.field<number>("UnitBase_ThunderNum").value}`);
                        parts.push(`hero.UnitApply_ThunderNum=${c.field<number>("UnitApply_ThunderNum").value}`);
                        parts.push(`hero.DoubleCast=${c.field<number>("UnitApply_ThunderDoubleCastNum").value}`);
                        break;
                    }
                }
            }
            const msg = parts.join(" ");
            if (msg !== last) { last = msg; ctx.log("[молнии] " + msg); }
        });
    },
});
