// zip-версия modkit/src/mods/satellite_more.ts

/**
 * Больше спутников в одном кругу. У героя два массива по 14 объектов спутников (группы A и B); круг делится на их число, поэтому при
 * десятках спутников (артефакты, комбинации) игра слипает их, а свыше 14 объектов для них просто нет.
 *
 * Мод делает две вещи:
 *  1. каждый кадр ограничивает UnitApply_SatelliteNum числом «предел игры» (14);
 *  2. остальные спутники (число − предел, не больше «доп. спутников») создаёт клонами центра первого спутника группы. У каждого спутника
 *     есть свой компонент C_Battle_AllyUnit_Satellite (анимация, столкновения, урон) — клон его несёт. Клоны встают в разрыв кольца
 *     (на тот же радиус, что у родных) и вращаются синхронно с группой. То же для группы B, когда игра её включила (обратное вращение).
 */
ModKit.define({
    id: "satellite_more",
    name: "Больше спутников",
    description: "Ровный круг при большом числе спутников; сверх 14 игра рисует клонами в разрыве кольца (группы A и B).",
    options: [
        { key: "cap", label: "Предел игры (спутников в группе)", default: 14, min: 1, max: 14, step: 1 },
        { key: "maxClones", label: "Доп. спутников клонами в группе (0 — выкл.)", default: 80, min: 0, max: 120, step: 1 },
    ],
    onEnable(ctx) {
        const quat = ctx.cls("UnityEngine.CoreModule:UnityEngine.Quaternion");
        const vec3 = ctx.cls("UnityEngine.CoreModule:UnityEngine.Vector3");
        const unityObject = ctx.cls("UnityEngine.CoreModule:UnityEngine.Object");
        const mkQuat = (x        , y        , z        , w        ) => { const o = quat.alloc(); o.method(".ctor", 4).invoke(x, y, z, w); return o.unbox(); };
        const mkVec = (x        , y        , z        ) => { const o = vec3.alloc(); o.method(".ctor", 3).invoke(x, y, z); return o.unbox(); };
        const destroy = (go               ) => { try { unityObject.method("Destroy", 1).invoke(go); } catch (_) { /* уже уничтожен */ } };
        const zOf = (t               ) => { const q = t.method               ("get_localRotation").invoke().handle; return 2 * Math.atan2(q.add(8).readFloat(), q.add(12).readFloat()); };
        const norm = (a        ) => { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; };
        const deg = (a        ) => Math.round(a * 180 / Math.PI);

        let hero                       = null;
        let heroPtr                       = null;
        let calls = 0, logged = 0, broken = false, frame = 0;
        let real = 0, lastWrite = -1, lastLoggedReal = -1, diagB = 0, diagBLogged = 0, offLogged                             = { A: false, B: false };
        const geoTick                            = { A: 0, B: 0 };
        const geoLogged                            = { A: 0, B: 0 };
        const lastSig                            = { A: "", B: "" };
        const groups                                     = { A: [], B: [] };   // GameObject клонов центра по группам

        const clearClones = () => { for (const g of ["A", "B"]         ) while (groups[g].length) destroy(groups[g].pop() ); };

        /** Клоны одной группы: число, расстановка в разрыв кольца и вращение. */
        const processGroup = (g           , cap        , want        )       => {
            const clones = groups[g];
            const centers = hero .field                             (`Array_Transform_Center_UnitSatellites${g}`).value;
            const sats = hero .field                             (`Array_Obj_UnitSatellites${g}`).value;
            if (centers.isNull() || sats.isNull()) return;
            const centerTf = centers.get(0), satGo = sats.get(0);
            // группа выключена (B включается только комбинацией игры) — клонов быть не должно
            const on = !!satGo && !satGo.isNull() && satGo.method         ("get_activeSelf").invoke();
            if (!centerTf || centerTf.isNull() || !on) {
                if (g === "B" && real > cap && !offLogged.B) { offLogged.B = true; ctx.log(`[satellite_more] B: группа выключена (объект ${!satGo || satGo.isNull() ? "нет" : "неактивен"}), клонов нет`); }
                while (clones.length) destroy(clones.pop() );
                return;
            }
            const satTf = satGo.method               ("get_transform").invoke();
            if (!satTf.method               ("get_parent").invoke().handle.equals(centerTf.handle)) {
                broken = true;
                ctx.log(`[satellite_more] группа ${g}: спутник не является прямым потомком центра — клонирование отключено`);
                return;
            }
            while (clones.length > want) destroy(clones.pop() );
            if (!want) return;
            const centerGo = centerTf.method               ("get_gameObject").invoke();
            while (clones.length < want) {
                // Копируем центр со спутником-ребёнком. Клоны висят под родным центром, поэтому копировать сам центр второй раз нельзя: он унёс бы
                // с собой уже созданные клоны (число объектов растёт вдвое с каждым клоном — игра зависает). Образец — первый клон (в нём только спутник).
                const clone = unityObject.method               ("Instantiate", 1).invoke(clones.length ? clones[0] : centerGo);
                const ctf = clone.method               ("get_transform").invoke();
                // клон — потомок родного центра: он вращается вместе с ним сам (игра крутит центр каждый кадр), нам нужен только постоянный сдвиг угла
                ctf.method("SetParent", 2).invoke(centerTf, false);
                ctf.method("set_localPosition").invoke(mkVec(0, 0, 0));
                lastSig[g] = "";
                clone.method("SetActive", 1).invoke(true);
                clones.push(clone);
            }
            // шаг между спутниками — по центрам 0 и 1 (с учётом направления вращения), клон k сдвинут на угол (cap + k)·шаг от центра 0.
            // Сдвиг постоянный, пока не изменился шаг или число клонов, поэтому каждый кадр ничего не пишем
            const base = zOf(centerTf);
            const c1 = centers.get(1);
            const step = c1 && !c1.isNull() ? norm(zOf(c1) - base) : (2 * Math.PI) / Math.max(real, 1);
            const sig = `${clones.length}:${Math.round(step * 1e4)}`;
            if (sig !== lastSig[g]) {
                lastSig[g] = sig;
                for (let k = 0; k < clones.length; k++) {
                    const a = (cap + k) * step;
                    clones[k].method               ("get_transform").invoke().method("set_localRotation").invoke(mkQuat(0, 0, Math.sin(a / 2), Math.cos(a / 2)));
                }
            }
            // геометрия кольца: углы всех спутников группы (родные + клоны) вокруг героя, наибольший разрыв — раз в ~5 с
            if (++geoTick[g] % 100 === 1 && geoLogged[g]++ < 12) {
                try {
                    const hp = hero .method               ("get_transform").invoke().method               ("get_position").invoke().handle;
                    const hx = hp.readFloat(), hy = hp.add(4).readFloat();
                    const angs           = [];
                    const radii           = [];
                    const addPos = (tf               ) => { const p = tf.method               ("get_position").invoke().handle; const dx = p.readFloat() - hx, dy = p.add(4).readFloat() - hy; angs.push(Math.atan2(dy, dx)); radii.push(Math.hypot(dx, dy)); };
                    for (let i = 0; i < sats.length; i++) { const o = sats.get(i); if (o && !o.isNull() && o.method         ("get_activeInHierarchy").invoke()) addPos(o.method               ("get_transform").invoke()); }
                    const nNat = angs.length;
                    for (const c of clones) { const ch = c.method               ("get_transform").invoke().method               ("GetChild").invoke(0); if (ch.method               ("get_gameObject").invoke().method         ("get_activeInHierarchy").invoke()) addPos(ch); }
                    const sorted = [...angs].sort((x, y) => x - y);
                    let maxGap = 0;
                    for (let i = 0; i < sorted.length; i++) { const nx = i + 1 < sorted.length ? sorted[i + 1] : sorted[0] + 2 * Math.PI; maxGap = Math.max(maxGap, nx - sorted[i]); }
                    const rmin = Math.min(...radii), rmax = Math.max(...radii);
                    ctx.log(`[satellite_more] кольцо ${g}: видно ${angs.length} (родных ${nNat}, клонов ${angs.length - nNat}), наибольший разрыв ${deg(maxGap)}° при ожидаемом ${deg((2 * Math.PI) / Math.max(real, 1))}°, радиус ${rmin.toFixed(1)}…${rmax.toFixed(1)}`);
                } catch (e) { ctx.log(`[satellite_more] геометрия ${g}: ${e}`); }
            }
            // диагностика группы B отдельным бюджетом: раз в ~3 с, пока она включена
            offLogged[g] = false;
            if (g === "B" && ++diagB % 180 === 1 && diagBLogged++ < 40) {
                ctx.log(`[satellite_more] B: спутников ${real}, клонов ${clones.length}, шаг ${deg(step)}° (ожидаемый ${deg((2 * Math.PI) / Math.max(real, 1))}°), база ${deg(base)}°, центр1 ${c1 && !c1.isNull() ? deg(zOf(c1)) + "°" : "нет"}`);
            }
            if (real !== lastLoggedReal && logged++ < 40) {
                ctx.log(`[satellite_more] группа ${g}: спутников ${real}, клонов ${clones.length}, шаг ${deg(step)}° (ожидаемый ${deg((2 * Math.PI) / Math.max(real, 1))}°), база ${deg(base)}°`);
            }
        };

        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (broken) return;
            try {
                if (!hero || ++calls % 60 === 0) {
                    const field = mgr.field               ("Obj_FieldAllyUnit").value;
                    hero = null;
                    if (field.isNull()) return;
                    const tf = field.method               ("get_transform").invoke();
                    const type = ctx.cls("C_Battle_AllyUnit").type.object;
                    for (let i = 0; i < tf.method        ("get_childCount").invoke(); i++) {
                        const c = tf.method               ("GetChild").invoke(i).method               ("get_gameObject").invoke()
                            .method               ("GetComponent", 1).invoke(type);
                        if (c && !c.isNull()) { hero = c; break; }
                    }
                    if (!hero) return;
                    if (!heroPtr || !heroPtr.equals(hero.handle)) { heroPtr = hero.handle; groups.A.length = 0; groups.B.length = 0; real = 0; lastWrite = -1; }   // новый бой: клоны ушли вместе со сценой
                }
                const cap = Math.round(ctx.settings.cap ?? 14);
                const f = hero.field        ("UnitApply_SatelliteNum");
                const v = Math.round(f.value);
                // игра пересчитала число — запоминаем настоящее; иначе v — наша же запись предела, настоящее число уже запомнено
                if (v > cap) { real = v; f.value = cap; lastWrite = cap; if (logged++ < 40) ctx.log(`[satellite_more] спутников ${v} → игре ${cap}`); }
                else if (!(v === cap && lastWrite === cap)) { real = v; lastWrite = -1; }

                const want = Math.min(Math.max(0, real - cap), Math.round(ctx.settings.maxClones ?? 80));
                if (want === 0 && !groups.A.length && !groups.B.length) { lastLoggedReal = real; return; }
                if (++frame % 3 === 0) { processGroup("A", cap, want); processGroup("B", cap, want); }
                lastLoggedReal = real;
            } catch (e) {
                broken = true;
                ctx.log(`[satellite_more] ошибка: ${e} — мод остановлен до перезапуска`);
            }
        });
        ctx.onCleanup(clearClones);
    },
});
