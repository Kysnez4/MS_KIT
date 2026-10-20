import { defineMod } from "../api";

/**
 * Больше спутников в одном кругу. У героя два массива по 14 объектов спутников (группы A и B); круг делится на их число, поэтому при
 * десятках спутников (артефакты, комбинации) игра слипает их, а свыше 14 объектов для них просто нет.
 *
 * Мод делает две вещи:
 *  1. каждый кадр ограничивает UnitApply_SatelliteNum числом «предел игры» (14);
 *  2. остальные спутники (число − предел, не больше «доп. спутников») создаёт клонами центра первого спутника группы. У каждого спутника
 *     есть свой компонент C_Battle_AllyUnit_Satellite (анимация, столкновения, урон) — клон его несёт. Клоны встают в разрыв кольца
 *     (на тот же радиус, что у родных) и вращаются синхронно с группой; размер, радиус орбиты и вложенные эффекты повторяют родной шар.
 *     То же для группы B, когда игра её включила (обратное вращение); шары B приводятся к размеру шара A.
 */
/** Сколько спутников в группе игра рисует сама (размер массивов объектов). */
const CAP = 14;
/** Сколько спутников в группе добавляем клонами сверх CAP. */
const MAX_CLONES = 80;

export default defineMod({
    id: "satellite_more",
    name: "Больше спутников",
    description: "Ровный круг при большом числе спутников; сверх 14 игра рисует клонами в разрыве кольца (группы A и B).",
    options: [],
    onEnable(ctx) {
        const quat = ctx.cls("UnityEngine.CoreModule:UnityEngine.Quaternion");
        const vec3 = ctx.cls("UnityEngine.CoreModule:UnityEngine.Vector3");
        const unityObject = ctx.cls("UnityEngine.CoreModule:UnityEngine.Object");
        const mkQuat = (x: number, y: number, z: number, w: number) => { const o = quat.alloc(); o.method(".ctor", 4).invoke(x, y, z, w); return o.unbox(); };
        const mkVec = (x: number, y: number, z: number) => { const o = vec3.alloc(); o.method(".ctor", 3).invoke(x, y, z); return o.unbox(); };
        const destroy = (go: Il2Cpp.Object) => { try { unityObject.method("Destroy", 1).invoke(go); } catch (_) { /* уже уничтожен */ } };
        const zOf = (t: Il2Cpp.Object) => { const q = t.method<Il2Cpp.Object>("get_localRotation").invoke().handle; return 2 * Math.atan2(q.add(8).readFloat(), q.add(12).readFloat()); };
        const norm = (a: number) => { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; };
        const deg = (a: number) => Math.round(a * 180 / Math.PI);

        let hero: Il2Cpp.Object | null = null;
        let heroPtr: NativePointer | null = null;
        let calls = 0, logged = 0, broken = false, frame = 0;
        let real = 0, lastWrite = -1;
        const lastSig: Record<"A" | "B", string> = { A: "", B: "" };
        const syncSig: Record<"A" | "B", string> = { A: "", B: "" };
        const syncTick: Record<"A" | "B", number> = { A: 0, B: 0 };
        const groups: Record<"A" | "B", Il2Cpp.Object[]> = { A: [], B: [] };   // GameObject клонов центра по группам

        const resetSigs = () => { lastSig.A = lastSig.B = syncSig.A = syncSig.B = ""; };
        const clearClones = () => { for (const g of ["A", "B"] as const) while (groups[g].length) destroy(groups[g].pop()!); resetSigs(); };

        /** Узел снимка дерева родного шара: масштаб, позиция корня, включённость вложенных объектов. */
        interface Node { sc: number[]; pos: number[] | null; on: boolean; kids: number; size: number }
        /** Снимок дерева родного шара в порядке обхода в глубину (глубина до 3). */
        const snapshot = (tf: Il2Cpp.Object, depth: number, root: boolean, out: Node[]): void => {
            const sc = tf.method<Il2Cpp.Object>("get_localScale").invoke().handle;
            const node: Node = { sc: [sc.readFloat(), sc.add(4).readFloat(), sc.add(8).readFloat()], pos: null, on: true, kids: 0, size: 0 };
            if (root) { const p = tf.method<Il2Cpp.Object>("get_localPosition").invoke().handle; node.pos = [p.readFloat(), p.add(4).readFloat(), p.add(8).readFloat()]; }
            else node.on = tf.method<Il2Cpp.Object>("get_gameObject").invoke().method<boolean>("get_activeSelf").invoke();
            const at = out.length;
            out.push(node);
            if (depth > 0) {
                node.kids = tf.method<number>("get_childCount").invoke();
                for (let i = 0; i < node.kids; i++) snapshot(tf.method<Il2Cpp.Object>("GetChild").invoke(i), depth - 1, false, out);
            }
            node.size = out.length - at;
        };
        /** Применяет снимок к дереву клона (то же строение, что у родного: клон скопирован с него). */
        const apply = (tf: Il2Cpp.Object, nodes: Node[], at: number, root: boolean): void => {
            const node = nodes[at];
            tf.method("set_localScale").invoke(mkVec(node.sc[0], node.sc[1], node.sc[2]));
            if (node.pos) tf.method("set_localPosition").invoke(mkVec(node.pos[0], node.pos[1], node.pos[2]));
            if (!root) {
                const go = tf.method<Il2Cpp.Object>("get_gameObject").invoke();
                if (go.method<boolean>("get_activeSelf").invoke() !== node.on) go.method("SetActive", 1).invoke(node.on);
            }
            const n = Math.min(node.kids, tf.method<number>("get_childCount").invoke());
            let next = at + 1;
            for (let i = 0; i < n; i++) { apply(tf.method<Il2Cpp.Object>("GetChild").invoke(i), nodes, next, false); next += nodes[next].size; }
        };

        /** Клоны одной группы: число, расстановка в разрыв кольца, вращение и повтор вида родного шара. */
        const processGroup = (g: "A" | "B", cap: number, want: number): void => {
            const clones = groups[g];
            const centers = hero!.field<Il2Cpp.Array<Il2Cpp.Object>>(`Array_Transform_Center_UnitSatellites${g}`).value;
            const sats = hero!.field<Il2Cpp.Array<Il2Cpp.Object>>(`Array_Obj_UnitSatellites${g}`).value;
            if (centers.isNull() || sats.isNull()) return;
            const centerTf = centers.get(0), satGo = sats.get(0);
            // группа выключена (B включается только комбинацией игры) — клонов быть не должно
            const on = !!satGo && !satGo.isNull() && satGo.method<boolean>("get_activeSelf").invoke();
            if (!centerTf || centerTf.isNull() || !on) {
                while (clones.length) destroy(clones.pop()!);
                lastSig[g] = syncSig[g] = "";
                return;
            }
            const satTf = satGo.method<Il2Cpp.Object>("get_transform").invoke();
            if (!satTf.method<Il2Cpp.Object>("get_parent").invoke().handle.equals(centerTf.handle)) {
                broken = true;
                ctx.log(`[satellite_more] группа ${g}: спутник не является прямым потомком центра — клонирование отключено`);
                return;
            }
            while (clones.length > want) destroy(clones.pop()!);
            if (!want) return;
            const centerGo = centerTf.method<Il2Cpp.Object>("get_gameObject").invoke();
            let created = false;
            while (clones.length < want) {
                // Копируем центр со спутником-ребёнком. Клоны висят под родным центром, поэтому копировать сам центр второй раз нельзя: он унёс бы
                // с собой уже созданные клоны (число объектов растёт вдвое с каждым клоном — игра зависает). Образец — первый клон (в нём только спутник).
                const clone = unityObject.method<Il2Cpp.Object>("Instantiate", 1).invoke(clones.length ? clones[0] : centerGo);
                const ctf = clone.method<Il2Cpp.Object>("get_transform").invoke();
                // клон — потомок родного центра: он вращается вместе с ним сам (игра крутит центр каждый кадр), нам нужен только постоянный сдвиг угла
                ctf.method("SetParent", 2).invoke(centerTf, false);
                ctf.method("set_localPosition").invoke(mkVec(0, 0, 0));
                // клон копирует масштаб центра, а висит под центром с тем же масштабом — без сброса шар вышел бы в квадрате
                ctf.method("set_localScale").invoke(mkVec(1, 1, 1));
                lastSig[g] = "";
                clone.method("SetActive", 1).invoke(true);
                clones.push(clone);
                created = true;
            }

            // Размер, радиус орбиты и вложенные эффекты родного шара игра меняет на лету (размер заклинания, комбы, артефакты), а клон помнит
            // значения на момент создания — повторяем у клонов вид родного шара. Шары B игра рисует крупнее A: приводим их к размеру шара A.
            // Дерево родного читаем раз в 3 вызова, клонам пишем только при изменении (и раз в ~3 с на случай, если клон сам что-то поменял).
            if (created || ++syncTick[g] % 3 === 0) {
                if (g === "B") {
                    const ag = hero!.field<Il2Cpp.Array<Il2Cpp.Object>>("Array_Obj_UnitSatellitesA").value;
                    const a0 = ag.isNull() ? null : ag.get(0);
                    if (a0 && !a0.isNull()) {
                        const r = a0.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("get_localScale").invoke().handle;
                        const sx = r.readFloat(), sy = r.add(4).readFloat(), sz = r.add(8).readFloat();
                        for (let i = 0; i < sats.length; i++) {
                            const o = sats.get(i);
                            if (!o || o.isNull()) continue;
                            const tf = o.method<Il2Cpp.Object>("get_transform").invoke();
                            const c = tf.method<Il2Cpp.Object>("get_localScale").invoke().handle;
                            if (Math.abs(c.readFloat() - sx) > 0.01 || Math.abs(c.add(4).readFloat() - sy) > 0.01) tf.method("set_localScale").invoke(mkVec(sx, sy, sz));
                        }
                    }
                }
                const nodes: Node[] = [];
                snapshot(satTf, 3, true, nodes);
                const r100 = (v: number) => Math.round(v * 100);
                const sig = `${clones.length}|` + nodes.map(n => n.sc.map(r100).join(",") + (n.pos ? ";" + n.pos.map(r100).join(",") : "") + (n.on ? "" : "x")).join("/");
                if (created || sig !== syncSig[g] || syncTick[g] % 60 === 0) {
                    syncSig[g] = sig;
                    for (const c of clones) apply(c.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.Object>("GetChild").invoke(0), nodes, 0, true);
                }
            }

            // шаг между спутниками — по центрам 0 и 1 (с учётом направления вращения), клон k сдвинут на угол (cap + k)·шаг от центра 0.
            // Сдвиг постоянный, пока не изменился шаг или число клонов, поэтому каждый кадр ничего не пишем
            const c1 = centers.get(1);
            const step = c1 && !c1.isNull() ? norm(zOf(c1) - zOf(centerTf)) : (2 * Math.PI) / Math.max(real, 1);
            const sig = `${clones.length}:${Math.round(step * 1e4)}`;
            if (sig !== lastSig[g]) {
                lastSig[g] = sig;
                for (let k = 0; k < clones.length; k++) {
                    const a = (cap + k) * step;
                    clones[k].method<Il2Cpp.Object>("get_transform").invoke().method("set_localRotation").invoke(mkQuat(0, 0, Math.sin(a / 2), Math.cos(a / 2)));
                }
                if (logged++ < 40) ctx.log(`[satellite_more] группа ${g}: спутников ${real}, клонов ${clones.length}, шаг ${deg(step)}° (ожидаемый ${deg((2 * Math.PI) / Math.max(real, 1))}°)`);
            }
        };

        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (broken) return;
            try {
                if (!hero || ++calls % 60 === 0) {
                    const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
                    hero = null;
                    if (field.isNull()) return;
                    const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
                    const type = ctx.cls("C_Battle_AllyUnit").type.object;
                    for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                        const c = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke()
                            .method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                        if (c && !c.isNull()) { hero = c; break; }
                    }
                    if (!hero) return;
                    if (!heroPtr || !heroPtr.equals(hero.handle)) { heroPtr = hero.handle; groups.A.length = 0; groups.B.length = 0; real = 0; lastWrite = -1; resetSigs(); }   // новый бой: клоны ушли вместе со сценой
                }
                const cap = CAP;
                const f = hero.field<number>("UnitApply_SatelliteNum");
                const v = Math.round(f.value);
                // игра пересчитала число — запоминаем настоящее; иначе v — наша же запись предела, настоящее число уже запомнено
                if (v > cap) { real = v; f.value = cap; lastWrite = cap; }
                else if (!(v === cap && lastWrite === cap)) { real = v; lastWrite = -1; }

                const want = Math.min(Math.max(0, real - cap), MAX_CLONES);
                if (want === 0 && !groups.A.length && !groups.B.length) return;
                if (++frame % 3 === 0) { processGroup("A", cap, want); processGroup("B", cap, want); }
            } catch (e) {
                broken = true;
                ctx.log(`[satellite_more] ошибка: ${e} — мод остановлен до перезапуска`);
            }
        });
        ctx.onCleanup(clearClones);
    },
});
