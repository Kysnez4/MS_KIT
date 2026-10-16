import { defineMod } from "../api";

/**
 * Больше видимых духов вокруг героя. Игра сама расставляет духов готовой фигурой (корутины Play_SpiritNN_Motion, N = 1…14), но если число
 * выросло мимо выбора способности, новые объекты остаются в центре героя (0,0), а свыше 14 их нет вовсе. У героя 28 объектов духов:
 * List_ObjSpirit 1…14 и List_ObjSpirit_Plus 1…14 (второй круг, который игра использует в ульте «Валькирия»). Мод расставляет их сам:
 * до 14 в первом кольце, остальные вторым кольцом снаружи. Родитель Plus-объектов (Unit_SpiritMid_Plus) выключен, поэтому их переносим
 * к родителю обычных духов.
 */
/** Духов в первом кольце (остальные — вторым). */
const FIRST_RING = 14;

export default defineMod({
    id: "spirit_more",
    name: "Больше видимых духов",
    description: "Показывает вокруг героя столько духов, сколько их в характеристиках (до 28), а не только 5.",
    options: [
        { key: "radius", label: "Масштаб колец ×0,1", default: 10, min: 5, max: 30, step: 1 },
    ],
    onEnable(ctx) {
        const shown = new Set<Il2Cpp.Object>();
        const vec3 = ctx.cls("UnityEngine.CoreModule:UnityEngine.Vector3");
        const mk3 = (x: number, y: number, z: number) => {
            const o = vec3.alloc();
            o.method(".ctor", 3).invoke(x, y, z);
            return o.unbox();
        };
        let calls = 0, logged = 0, rendererFailed = false;
        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (++calls % 20 !== 0) return;
            const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
            if (field.isNull()) return;
            const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
            const type = ctx.cls("C_Battle_AllyUnit").type.object;
            let hero: Il2Cpp.Object | null = null;
            for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                const c = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke()
                    .method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                if (c && !c.isNull()) { hero = c; break; }
            }
            if (!hero) return;
            const total = Math.round(hero.field<number>("UnitApply_SpiritNum").value);
            const k = (ctx.settings.radius ?? 10) / 10;
            const mid = hero.field<Il2Cpp.Object>("List_ObjSpirit").value;
            const plus = hero.field<Il2Cpp.Object>("List_ObjSpirit_Plus").value;
            if (mid.isNull() || plus.isNull()) return;
            const midItems = mid.field<Il2Cpp.Array<Il2Cpp.Object>>("_items").value;
            const plusItems = plus.field<Il2Cpp.Array<Il2Cpp.Object>>("_items").value;
            // объекты по порядку: Mid 1…14, затем Plus 1…14 (всего 28)
            const objAt = (i: number): Il2Cpp.Object | null => {
                const go = i < 14 ? midItems.get(i + 1) : plusItems.get(i - 14 + 1);
                return go && !go.isNull() ? go : null;
            };
            // до 14 духов у игры есть свои фигуры: вмешиваемся, только если она не расставила кого-то
            // (объект выключен или стоит в центре героя) или духов больше FIRST_RING
            const stuck = (i: number): boolean => {
                const go = objAt(i);
                if (!go) return false;
                if (!go.method<boolean>("get_activeSelf").invoke()) return true;
                const p = go.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_localPosition").invoke().handle;
                return Math.abs(p.readFloat()) < 0.01 && Math.abs(p.add(4).readFloat()) < 0.01;
            };
            let needs = total > FIRST_RING;
            for (let i = 0; !needs && i < Math.min(14, total); i++) needs = stuck(i);
            if (!needs) return;
            const n = Math.min(28, total);
            // первое кольцо — до FIRST_RING духов равномерно от верха (симметрично), второе — остальные снаружи
            const c1 = Math.min(FIRST_RING, n), c2 = n - c1;
            const r1 = Math.max(0.55, c1 * 0.06), r2 = r1 + 0.5;
            const place = (i: number): [number, number] => {
                const first = i < c1;
                const cnt = first ? c1 : c2, idx = first ? i : i - c1, rad = (first ? r1 : r2) * k;
                const a = (idx - (cnt - 1) / 2) * (Math.PI * 2 / cnt) + (first ? 0 : Math.PI / cnt);
                return [rad * Math.sin(a), 0.05 + rad * Math.cos(a)];
            };
            let changed = 0;
            for (let i = 0; i < n; i++) {
                const go = objAt(i);
                if (!go) continue;
                const t = go.method<Il2Cpp.Object>("get_transform").invoke();
                const [x, y] = place(i);
                const cur = t.method<Il2Cpp.Object>("get_localPosition").invoke().handle;
                const z = cur.add(8).readFloat();
                if (Math.abs(cur.readFloat() - x) > 0.02 || Math.abs(cur.add(4).readFloat() - y) > 0.02) { t.method("set_localPosition").invoke(mk3(x, y, z)); changed++; }
                if (!go.method<boolean>("get_activeSelf").invoke()) { go.method("SetActive").invoke(true); changed++; }
                // объект Plus висит в выключенном родителе Unit_SpiritMid_Plus (активен сам, но не в иерархии) и не рисуется: переносим к родителю духов первого круга
                if (i >= 14 && !go.method<boolean>("get_activeInHierarchy").invoke()) {
                    const mp = objAt(0)!.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_parent").invoke();
                    t.method("SetParent", 2).invoke(mp, false);
                    t.method("set_localPosition").invoke(mk3(x, y, z));
                    changed++;
                }
                shown.add(go);
            }
            // спрайт и размер у духов второго круга берём у духа первого круга с тем же номером (sortingOrder/enabled у Renderer в игре вырезаны)
            const rp = hero.field<Il2Cpp.Object>("List_SpriteRenderer_Spirit_Plus").value;
            const rm = hero.field<Il2Cpp.Object>("List_SpriteRenderer_Spirit").value;
            if (!rp.isNull() && !rm.isNull() && n > 14 && !rendererFailed) try {
                const pItems = rp.field<Il2Cpp.Array<Il2Cpp.Object>>("_items").value;
                const mItems = rm.field<Il2Cpp.Array<Il2Cpp.Object>>("_items").value;
                for (let i = 1; i <= Math.min(14, n - 14); i++) {
                    const pr = pItems.get(i), mr = mItems.get(i);
                    if (!pr || pr.isNull() || !mr || mr.isNull()) continue;
                    try {
                        pr.method("set_sprite").invoke(mr.method<Il2Cpp.Object>("get_sprite").invoke());
                        const ms = mr.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_localScale").invoke().handle;
                        pr.method<Il2Cpp.Object>("get_transform").invoke().method("set_localScale").invoke(mk3(ms.readFloat(), ms.add(4).readFloat(), ms.add(8).readFloat()));
                    } catch (e) { if (logged++ < 30) ctx.log(`[spirit_more] рендерер Plus[${i}]: ${e}`); }
                }
            } catch (e) { rendererFailed = true; ctx.log(`[spirit_more] рендереры Plus: ${e} — больше не трогаю`); }
            if (changed && logged++ < 30) ctx.log(`[spirit_more] духов ${total}: переставлено/включено объектов ${changed}`);
        });
        ctx.onCleanup(() => { for (const go of shown) { try { go.method("SetActive").invoke(false); } catch (_) { /* уничтожен */ } } shown.clear(); });
    },
});
