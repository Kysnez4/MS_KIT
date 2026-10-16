import { defineMod } from "../api";

/**
 * Отладка: почему при большом числе спутников/духов перестают вращаться спутники и атаковать духи.
 * Пишет в лог:
 *  - состояние героя (число спутников/духов, длины массивов спутников, FPS) при изменении и раз в ~10 с;
 *  - момент завершения корутин вращения спутников и атаки духов (корутина, которая упала или закончилась, больше не запускается);
 *  - исключения, которые игра отправляет в Unity (DebugLogHandler.LogException), с текстом и стеком;
 *  - запуски групп спутников (ApplySatelliteGroup) с длиной массива.
 * Лог: modkit.log, строки с «[orbit]».
 */
export default defineMod({
    id: "orbitprobe",
    name: "Зонд спутников и духов (отладка)",
    description: "Пишет в лог число спутников/духов, длины массивов, FPS, исключения и завершение корутин вращения/атаки.",
    options: [],
    onEnable(ctx) {
        const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const hooks: InvocationListener[] = [];
        const t0 = Date.now();
        const stamp = () => ((Date.now() - t0) / 1000).toFixed(1) + "с";
        const counters = new Map<string, number>();
        const capped = (key: string, max: number) => {
            const c = (counters.get(key) ?? 0) + 1;
            counters.set(key, c);
            return c <= max;
        };

        // --- завершение корутин (MoveNext вернул false)
        const watched: string[] = [];
        for (const k of img.classes) {
            if (!/^<Play_(SatelliteMid_Rotation|Spirit_Attack)/.test(k.name)) continue;
            const m = k.tryMethod("MoveNext", 0);
            if (!m) continue;
            watched.push(k.name);
            const name = k.name;
            hooks.push(Interceptor.attach(m.virtualAddress, {
                onLeave(ret) {
                    if (ret.toInt32() & 0xff) return;
                    if (capped("end:" + name, 40)) ctx.log(`[orbit] ${stamp()} корутина ${name} ЗАВЕРШИЛАСЬ`);
                },
            }));
        }
        ctx.log("[orbit] слежу за корутинами: " + (watched.join(", ") || "не найдены"));

        // --- исключения, дошедшие до Unity
        const lh = ctx.cls("UnityEngine.CoreModule:UnityEngine.DebugLogHandler").tryMethod("LogException", 2);
        if (lh) {
            const seen = new Set<string>();
            hooks.push(Interceptor.attach(lh.virtualAddress, {
                onEnter(args) {
                    try {
                        const ex = new Il2Cpp.Object(args[1]);
                        const msg = String(ex.method<Il2Cpp.String>("get_Message").invoke());
                        if (seen.has(msg) || seen.size >= 30) return;
                        seen.add(msg);
                        const st = String(ex.method<Il2Cpp.String>("get_StackTrace").invoke());
                        ctx.log(`[orbit] ${stamp()} ИСКЛЮЧЕНИЕ: ${msg}\n${st.split("\n").slice(0, 6).join("\n")}`);
                    } catch (e) { ctx.log("[orbit] чтение исключения: " + e); }
                },
            }));
        } else ctx.log("[orbit] нет DebugLogHandler.LogException");

        // --- запуски групп спутников
        const grp = img.class("C_Battle_AllyUnit").tryMethod("ApplySatelliteGroup", 4);
        if (grp) {
            hooks.push(Interceptor.attach(grp.virtualAddress, {
                onEnter(args) {
                    if (!capped("grp", 30)) return;
                    try {
                        const objs = args[1].isNull() ? -1 : new Il2Cpp.Array<any>(args[1]).length;
                        const name = args[4].isNull() ? "null" : new Il2Cpp.String(args[4]).content;
                        ctx.log(`[orbit] ${stamp()} ApplySatelliteGroup(${name}), объектов в массиве: ${objs}`);
                    } catch (e) { ctx.log("[orbit] ApplySatelliteGroup: " + e); }
                },
            }));
        }

        // --- состояние героя
        const time = ctx.cls("UnityEngine.CoreModule:UnityEngine.Time");
        const arrLen = (c: Il2Cpp.Object, f: string): string => {
            try {
                const a = c.field<Il2Cpp.Array<any>>(f).value;
                if (a.isNull()) return "null";
                let live = 0;
                for (let i = 0; i < a.length; i++) if (!a.get(i)?.isNull?.()) live++;
                return `${a.length}(живых ${live})`;
            } catch (_) { return "?"; }
        };
        const listCount = (c: Il2Cpp.Object, f: string): string => {
            try {
                const l = c.field<Il2Cpp.Object>(f).value;
                return l.isNull() ? "null" : String(l.field<number>("_size").value);
            } catch (_) { return "?"; }
        };
        /** Какие объекты списка духов видны: индекс:x,y (локальная позиция) только для активных. */
        const spiritView = (c: Il2Cpp.Object, f: string): string => {
            try {
                const l = c.field<Il2Cpp.Object>(f).value;
                if (l.isNull()) return "null";
                const n = l.field<number>("_size").value;
                const items = l.field<Il2Cpp.Array<Il2Cpp.Object>>("_items").value;
                const act: string[] = [];
                for (let i = 0; i < n; i++) {
                    const go = items.get(i);
                    if (!go || go.isNull() || !go.method<boolean>("get_activeSelf").invoke()) continue;
                    const p = go.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_localPosition").invoke();
                    const v = p.handle;      // Vector3: три float подряд
                    act.push(`${i}:${v.readFloat().toFixed(1)},${v.add(4).readFloat().toFixed(1)}`);
                }
                return `${act.length} активных [${act.join(" ")}]`;
            } catch (e) { return "?" + e; }
        };
        /** Спутники: массив объектов — сколько активно и локальные позиции (индекс:x,y), флаги присутствия. */
        const satView = (c: Il2Cpp.Object, arr: string, flags: string): string => {
            try {
                const a = c.field<Il2Cpp.Array<Il2Cpp.Object>>(arr).value;
                if (a.isNull()) return "null";
                const act: string[] = [];
                for (let i = 0; i < a.length; i++) {
                    const go = a.get(i);
                    if (!go || go.isNull() || !go.method<boolean>("get_activeSelf").invoke()) continue;
                    const v = go.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_localPosition").invoke().handle;
                    act.push(`${i}:${v.readFloat().toFixed(1)},${v.add(4).readFloat().toFixed(1)}`);
                }
                // угол поворота центров (родителей спутников): если он меняется между строками лога — вращение идёт
                let rot = "";
                try {
                    const cen = c.field<Il2Cpp.Array<Il2Cpp.Object>>(arr.replace("Obj_UnitSatellites", "Transform_Center_UnitSatellites")).value;
                    const zs: string[] = [];
                    for (let i = 0; i < Math.min(3, cen.length); i++) zs.push(cen.get(i).method<Il2Cpp.Object>("get_localEulerAngles").invoke().handle.add(8).readFloat().toFixed(0));
                    rot = ` угол центров z=[${zs.join(",")}]`;
                } catch (_) { rot = " угол центров ?"; }
                const f = c.field<Il2Cpp.Array<number>>(flags).value;
                let on = 0;
                if (!f.isNull()) for (let i = 0; i < f.length; i++) if (f.get(i)) on++;
                return `${act.length} акт. флагов ${on}${rot}`;
            } catch (e) { return "?" + e; }
        };
        let calls = 0, last = "", lastFull = 0;
        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (++calls % 60 !== 0) return;
            if (capped("alive", 1)) ctx.log("[orbit] хук C_Battle_Manager.Update работает");
            try {
                const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
                if (field.isNull()) { if (capped("nofield", 3)) ctx.log("[orbit] Obj_FieldAllyUnit == null"); return; }
                const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
                const type = ctx.cls("C_Battle_AllyUnit").type.object;
                let hero: Il2Cpp.Object | null = null;
                for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                    const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
                    const c = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                    if (c && !c.isNull()) { hero = c; break; }
                }
                if (!hero) { if (capped("nohero", 3)) ctx.log("[orbit] герой не найден среди детей поля"); return; }
                const fps = 1 / Math.max(1e-4, time.method<number>("get_unscaledDeltaTime").invoke());
                const num = (f: string) => { try { return Math.round(hero!.field<number>(f).value * 100) / 100; } catch (_) { return "?"; } };
                const state =
                    `спутники: UnitApply=${num("UnitApply_SatelliteNum")} SatelliteNum=${num("SatelliteNum")} ` +
                    `A=${arrLen(hero, "Array_Obj_UnitSatellitesA")} B=${arrLen(hero, "Array_Obj_UnitSatellitesB")} | ` +
                    `духи: UnitApply=${num("UnitApply_SpiritNum")} List_ObjSpirit=${listCount(hero, "List_ObjSpirit")} ` +
                    `Plus=${listCount(hero, "List_ObjSpirit_Plus")} | видны: Mid ${spiritView(hero, "List_ObjSpirit")}; Plus ${spiritView(hero, "List_ObjSpirit_Plus")} | ` +
                    `FireNum=${num("Apply_SpiritFireNum")} PSpeed=${num("UnitApply_SpiritPSpeed")} | ` +
                    `спутники видны: A ${satView(hero, "Array_Obj_UnitSatellitesA", "Array_IsSatelliteA_P")}; B ${satView(hero, "Array_Obj_UnitSatellitesB", "Array_IsSatelliteB_P")} rotVar=${num("SatelliteRotationVar")}`;
                const now = Date.now();
                if (state !== last || now - lastFull > 10000) {
                    last = state;
                    lastFull = now;
                    ctx.log(`[orbit] ${stamp()} FPS≈${fps.toFixed(0)} ${state}`);
                }
            } catch (e) { if (capped("state-err", 5)) ctx.log("[orbit] состояние: " + e); }
        });

        ctx.onCleanup(() => hooks.forEach(h => h.detach()));
    },
});
