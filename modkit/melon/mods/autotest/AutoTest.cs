using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Бот-тестер. Внутри боя сам двигает героя, выбирает артефакты из сундуков и пишет результат в UserData/ModKit/autotest.txt:
    /// HB (пульс), STAT поле=значение (снимки полей героя), EVENT, CHECK имя PASS|FAIL, затем RESULT PASS|FAIL и DONE.
    /// Снаружи его запускает tools/autotest.py (вход в бой, ожидание DONE или вылета, разбор отчёта о вылете, перебор модов).
    /// </summary>
    public class AutoTestMod : IKitMod
    {
        private static readonly string OutPath = Path.Combine(Paths.Dir, "autotest.txt");

        public ModDef Define()
        {
            var def = new ModDef
            {
                Id = "autotest", Name = "Бот-тестер", Version = "1.1",
                Description = "Сам ходит по бою и выбирает артефакты, пишет результат в autotest.txt. Запускается скриптом tools/autotest.py.",
                OnEnable = OnEnable,
            };
            def.Options.Add(new ModOption { Key = "duration", Label = "Длительность боя, с", Default = 150, Min = 10, Max = 3600, Step = 10 });
            def.Options.Add(new ModOption { Key = "move", Label = "Ходить к шарикам и сундуку (0 стоять)", Default = 1, Min = 0, Max = 1, Step = 1 });
            def.Options.Add(new ModOption { Key = "pick", Label = "Выбирать артефакты из сундуков", Default = 1, Min = 0, Max = 1, Step = 1 });
            def.Options.Add(new ModOption { Key = "picks", Label = "Сколько артефактов выбрать до конца теста (0 — по времени)", Default = 0, Min = 0, Max = 50, Step = 1 });
            def.Options.Add(new ModOption { Key = "spells", Label = "Выбирать заклинания при повышении уровня", Default = 1, Min = 0, Max = 1, Step = 1 });
            def.Options.Add(new ModOption { Key = "forceoffer", Label = "Вызывать предложение артефакта самому раз в N с (0 — ждать сундук)", Default = 0, Min = 0, Max = 600, Step = 5 });
            def.Options.Add(new ModOption { Key = "flee", Label = "Круг уклонения от врагов, ед.", Default = 3, Min = 0, Max = 30, Step = 1 });
            def.Options.Add(new ModOption { Key = "search", Label = "Радиус поиска шариков, ед.", Default = 14, Min = 2, Max = 60, Step = 1 });
            def.Options.Add(new ModOption { Key = "magnet", Label = "Магнит: прибавка к радиусу сбора предметов (0 выкл)", Default = 0, Min = 0, Max = 100, Step = 5 });
            def.Options.Add(new ModOption { Key = "stats", Label = "Поля героя для снимков (через запятую)", Default = 0, Min = 0, Max = 0, Step = 1, Hidden = true });
            return def;
        }

        /// <summary>Поля героя в снимках STAT (имена как в gamelog).</summary>
        private static readonly string[] StatFields =
        {
            "UnitApply_MagicBulletNum", "UnitApply_MagicBulletDamage", "UnitApply_ThunderNum", "UnitApply_ThunderDamage",
            "UnitApply_SatelliteNum", "UnitApply_SpiritNum", "UnitApply_HpMax", "UnitApply_MagicCooltimeRatio", "UnitApply_ItemObtainArea", "UnitBase_ItemObtainArea",
        };

        private static void W(string s)
        {
            try { File.AppendAllText(OutPath, $"[{DateTime.Now:HH:mm:ss}] {s}\n"); } catch { /* */ }
            Log.Write("[autotest] " + s);
        }



        /// <summary>Сверяет счётчики синергий игры (Array_SynergyObtain) с подсчётом по таблице Synergy и списку полученных артефактов.</summary>
        private static void CheckSynergies()
        {
            var have = new HashSet<int>();
            var l = Z_Database.List_ArtifactObtain;
            for (int i = 0; i < l.Count; i++) have.Add(l[i]);
            var t = new Table("Synergy");
            var arr = Z_Database.Array_SynergyObtain;
            int bad = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (!(t.Get(i, t.IdColumn) is double id)) continue;
                int sid = (int)id, expect = 0;
                for (int k = 1; k <= 5; k++) { int a = (int)t.Num(i, $"ID_0{k}"); if (a > 0 && have.Contains(a)) expect++; }
                int got = sid >= 0 && sid < arr.Length ? arr[sid] : -1;
                if (got != expect) { bad++; W($"EVENT синергия {sid} «{t.Get(i, "이름")}»: счётчик игры {got}, по артефактам {expect} (нужно {t.Num(i, "개수")})"); }
            }
            W($"CHECK счётчики синергий совпадают с артефактами (расхождений: {bad}) {(bad == 0 ? "PASS" : "FAIL")}");
        }

        /// <summary>Нажимает кнопку интерфейса как палец: вызывает onClick первой найденной Button на объекте или его детях.</summary>
        private static bool Click(GameObject go, string what)
        {
            if (go == null) { W($"EVENT нет объекта «{what}»"); return false; }
            var btn = go.GetComponent<UnityEngine.UI.Button>() ?? go.GetComponentInChildren<UnityEngine.UI.Button>();
            if (btn == null) { W($"EVENT на «{what}» нет Button"); return false; }
            // как у настоящего касания: кнопка становится выбранной в EventSystem (игра читает currentSelectedGameObject, без этого — NullReference)
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(btn.gameObject);
            btn.onClick.Invoke();
            return true;
        }

        private static double FleeRadius = 3.0, DangerRadius = 4.0, SearchRadius = 14;
        private static int _lastDir = -1;
        private static string FirstLine(Exception e) { var m = e.Message ?? ""; int i = m.IndexOf((char)10); return i < 0 ? m : m.Substring(0, i); }

        private static bool _dumpedButtons, _dumpedAbilityButtons;

        /// <summary>Пишет в журнал все Button объекта и его детей: путь и методы, которые вызывает onClick (чтобы нажимать нужную).</summary>
        private static void DumpButtons(GameObject go, string what)
        {
            if (go == null) { W($"BTN {what}: объекта нет"); return; }
            var all = go.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            W($"BTN {what} «{go.name}» active={go.activeInHierarchy}: кнопок {all.Length}");
            foreach (var b in all)
            {
                var names = new List<string>();
                try
                {
                    int n = b.onClick.GetPersistentEventCount();
                    for (int i = 0; i < n; i++) names.Add($"{b.onClick.GetPersistentTarget(i)?.name}.{b.onClick.GetPersistentMethodName(i)}");
                }
                catch (Exception e) { names.Add("? " + FirstLine(e)); }
                W($"BTN   «{b.gameObject.name}» active={b.gameObject.activeInHierarchy} interactable={b.interactable} onClick=[{string.Join("; ", names)}]");
            }
        }

        private static Vector3? _lastGoal;

        private static double Dist(Vector3 a, Vector3 b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        /// <summary>Куда идти: к сундуку (по пути подбирая шарики рядом с линией), иначе к ближайшему шарику. Возвращает вектор джойстика.</summary>
        private static Vector3 Navigate(C_Battle_AllyUnit hero, ref string target)
        {
            var me = hero.transform.position;
            var enemies = new List<Vector3>();
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<C_Battle_EnemyUnit>()))
            {
                var en = o.TryCast<C_Battle_EnemyUnit>();
                if (en != null && en.gameObject.activeInHierarchy) enemies.Add(en.transform.position);
            }
            Vector3? chest = null; double best = 1e9; int chests = 0;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<C_Battle_Item_TreasureBox>()))
            {
                var b = o.TryCast<C_Battle_Item_TreasureBox>();
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                chests++;
                var cb = b.Transform_ObjItem;
                var p = cb != null ? cb.position : b.transform.position; double d = Dist(me, p);
                if (d < best) { best = d; chest = p; }
            }
            Vector3? goal = null; string what = "нет";
            var items = new List<Vector3>(); int pool = 0;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<C_Battle_Item>()))
            {
                var it = o.TryCast<C_Battle_Item>();
                if (it == null || !it.gameObject.activeInHierarchy) continue;
                // скрипт сидит на контейнере; сам шарик — Transform_ObjItem (его позицию и берём)
                var body = it.Transform_ObjItem;
                if (body == null || !body.gameObject.activeInHierarchy) continue;
                pool++;
                if (!it.IsItemActiveON) continue;                       // в пуле много неактивных шариков: берём только настоящие
                items.Add(body.position);
            }
            // шарики рядом (до 14 ед.) важнее: идём к лучшему по «расстояние до героя + четверть расстояния от шарика до сундука»; сундук — когда шариков рядом нет
            double bestItem = 1e9; Vector3? itemGoal = null; double itemDist = 0;
            foreach (var p in items)
            {
                double d = Dist(me, p);
                if (d < 0.35 || d > SearchRadius) continue;                                  // уже под героем или слишком далеко
                double danger = 0;                                              // враги рядом с шариком: чем ближе и больше, тем хуже
                foreach (var e in enemies) { double de = Dist(p, e); if (de < DangerRadius) danger += (DangerRadius - de) * 2; }
                double score = d + danger + (chest.HasValue ? Dist(p, chest.Value) * 0.25 : 0);
                if (score < bestItem) { bestItem = score; itemGoal = p; itemDist = d; }
            }
            // приклеивание: пока прежняя цель (шарик в пределах 0,8 от неё) жива и ещё не взята, не переключаемся
            if (_lastGoal.HasValue)
                foreach (var p in items)
                    if (Dist(p, _lastGoal.Value) < 0.8 && Dist(me, p) > 0.35 && Dist(me, p) <= SearchRadius) { itemGoal = p; itemDist = Dist(me, p); break; }
            if (chest.HasValue) { goal = chest; what = $"СУНДУК {best:0.#} (сундуков {chests})"; _lastGoal = null; }
            else if (itemGoal.HasValue) { goal = itemGoal; what = $"шарик {itemDist:0.#} (активных {items.Count} из {pool})"; _lastGoal = itemGoal; }
            else
            {
                double bd = 1e9;
                foreach (var p in items) { double d = Dist(me, p); if (d >= 0.35 && d < bd) { bd = d; goal = p; what = $"далёкий шарик {d:0}"; } }
            }
            if (!itemGoal.HasValue) _lastGoal = null;
            target = what;
            if (!goal.HasValue) goal = me;
            double dx = goal.Value.x - me.x, dy = goal.Value.y - me.y, l = Math.Sqrt(dx * dx + dy * dy);
            double gx = l < 0.15 ? 0 : dx / l, gy = l < 0.15 ? 0 : dy / l;
            // 16 направлений: выигрывает то, что ближе к цели и дальше от толпы (в окружении выбирается самый свободный путь)
            int near = enemies.Count(e => Dist(me, e) < FleeRadius);
            // к сундуку идём напролом: цель важнее опасности (у босса бот раньше вечно отбегал от сундука)
            bool toChest = chest.HasValue;
            double goalW = toChest ? 3.5 : 1.5, dangerW = toChest ? 0.12 : 0.35;
            double bestScore = -1e9; int bestDir = -1;
            for (int k = 0; k < 16; k++)
            {
                double ang = k * Math.PI / 8, ux = Math.Cos(ang), uy = Math.Sin(ang);
                double px = me.x + ux * 2.0, py = me.y + uy * 2.0, danger = 0;
                foreach (var e in enemies)
                {
                    double ddx = px - e.x, ddy = py - e.y, de = Math.Sqrt(ddx * ddx + ddy * ddy);
                    if (de < FleeRadius) danger += FleeRadius - de;
                }
                double score = (ux * gx + uy * gy) * goalW - danger * dangerW + (k == _lastDir ? 0.3 : 0);
                if (score > bestScore) { bestScore = score; bestDir = k; }
            }
            if (near > 0) target += $" · врагов рядом {near}";
            if (near == 0 && l < 0.15) { _lastDir = -1; return Vector3.zero; }
            _lastDir = bestDir;
            double a2 = bestDir * Math.PI / 8;
            return new Vector3((float)Math.Cos(a2), (float)Math.Sin(a2), 0);
        }

        private static void OnEnable(ModContext ctx)
        {
            try { File.WriteAllText(OutPath, ""); } catch { /* */ }
            W("запуск бота");
            C_Battle_Manager mgr = null;
            V_Artifact_Manager art = null;
            C_Battle_AllyUnit hero = null;
            double battleAt = -1, lastHb = 0, lastStat = 0, dirChangeAt = 0, offerSince = -1, pickedAt = -1;
            int deadTicks = 0, picks = 0, countBefore = 0, failedPicks = 0, spellPicks = 0;
            V_Ability_Manager abil = null;
            Vector3 stick = Vector3.zero;            // вектор «джойстика», применяется каждый кадр
            double abilitySince = -1, lastNav = 0, lastTry = 0, lastForce = 0, stepAt = 0;
            int pickStep = 0, spellStep = 0;
            double spellStepAt = 0;
            string navTarget = "нет";
            bool done = false;
            var props = new Dictionary<string, PropertyInfo>();
            foreach (var f in StatFields) props[f] = typeof(C_Battle_AllyUnit).GetProperty(f);

            ctx.Before("C_Battle_Manager", "Update", 0, (s, a) => mgr = (C_Battle_Manager)s);
            // «магнит»: радиус сбора предметов героя + magnet (через проход ядра: база игры не копится, при выключении возвращается)
            ctx.Use<IEffects>("modkit_effects").OnHeroPass(h => { double m = ctx.Setting("magnet"); if (m > 0) h.Field("UnitApply_ItemObtainArea").Value += m; });
            ctx.After("V_Ability_Manager", "Create_Ability", 0, (s, a) => abil = (V_Ability_Manager)s);
            ctx.Before("V_Ability_Manager", "AbilityStatApply", 1, (s, a) => abil = (V_Ability_Manager)s);
            // ходьба как у человека: подставляем вектор джойстика каждый кадр, дальше двигает сама игра (анимация, скорость, коллизии)
            ctx.After("V_JoyStickController", "HandleInput", 0, (s, a) => { if (!done && stick != Vector3.zero) V_JoyStickController._moveVector = stick; });
            ctx.Before("V_JoyStickController", "FixedUpdate", 0, (s, a) => { if (!done && stick != Vector3.zero) V_JoyStickController._moveVector = stick; });
            ctx.After("V_Artifact_Manager", "Create_Artifact", 0, (s, a) => { art = (V_Artifact_Manager)s; W("EVENT предложен артефакт (Create_Artifact)"); });

            C_Battle_AllyUnit FindHero()
            {
                if (hero != null && hero.gameObject != null) return hero;
                hero = null;
                var field = mgr?.Obj_FieldAllyUnit;
                if (field == null) return null;
                var tf = field.transform;
                for (int i = 0; i < tf.childCount; i++)
                {
                    var c = tf.GetChild(i).gameObject.GetComponent<C_Battle_AllyUnit>();
                    if (c != null) return hero = c;
                }
                return null;
            }

            // ArtifactType после сундука не сбрасывается в 0, поэтому окно предложения определяем по его объекту
            bool OfferOpen()
            {
                try { var f = art?.Obj_Field_ArtifactSelect; return f != null && f.activeInHierarchy; }
                catch { return false; }
            }

            int ObtainCount() { try { return Z_Database.List_ArtifactObtain.Count; } catch { return -1; } }

            void Finish(string result, string why)
            {
                if (done) return;
                done = true;
                stick = Vector3.zero;                    // отдаём управление: джойстик больше не подменяется
                W($"CHECK ошибок модов за тест: {Crash.TotalErrors} {(Crash.TotalErrors == 0 ? "PASS" : "FAIL")}");
                if (Crash.TotalErrors > 0 && result == "PASS") { result = "FAIL"; why = "были ошибки модов"; }
                W($"RESULT {result} {why}");
                W("DONE");
            }

            ctx.Every(200, () =>
            {
                if (done) return;
                double now = Scheduler.Now;
                var h = FindHero();
                if (h == null) { battleAt = -1; return; }
                if (battleAt < 0) { battleAt = now; lastHb = lastStat = now; W("EVENT герой найден, бой идёт"); }
                double t = now - battleAt;

                bool busy = OfferOpen() || V_Ability_Manager.Ability_SelectON;
                if (ctx.Setting("move") > 0 && !busy)
                {
                    if (now - lastNav > 0.25) { lastNav = now; ctx.Guard("навигация", () => { FleeRadius = ctx.Setting("flee"); DangerRadius = FleeRadius + 1; SearchRadius = ctx.Setting("search"); stick = Navigate(h, ref navTarget); }); }
                }
                else stick = Vector3.zero;

                // выбор заклинания при повышении уровня
                if (V_Ability_Manager.Ability_SelectON && ctx.Setting("spells") > 0)
                {
                    if (abilitySince < 0) { abilitySince = now; W("EVENT окно выбора заклинания открыто"); }
                    if (now - abilitySince > 1.5)
                    {
                        ctx.Guard("выбор заклинания", () =>
                        {
                            // клик по карточке заклинания, как палец (прямые Button_* игры из таймера падали с IndexOutOfRange)
                            var cards = abil.List_ObjAbility;
                            var live = new List<int>();
                            for (int i = 0; i < cards.Count; i++) if (cards[i] != null && cards[i].activeInHierarchy) live.Add(i);
                            if (live.Count == 0) { W("EVENT нет активных карточек заклинаний"); return; }
                            if (!_dumpedAbilityButtons)
                            {
                                _dumpedAbilityButtons = true;
                                DumpButtons(cards[live[0]], "карточка заклинания");
                                DumpButtons(abil.Obj_ButtonAbilityCh_Decision, "подтвердить заклинание");
                            }
                            // два шага, как у артефактов: карточка (Button_AbilityCh), затем «Decision»; без второго клика окно не закрывается
                            if (spellStep == 0)
                            {
                                int pick = live[spellPicks % live.Count];       // по кругу, чтобы пробовать разные заклинания
                                ctx.Mark($"клик по карточке заклинания {pick}");
                                W($"EVENT заклинание шаг 1: карточка {pick} из {live.Count}");
                                if (Click(cards[pick], "заклинание")) { spellStep = 1; spellStepAt = now; }
                            }
                            else if (now - spellStepAt > 1.0)
                            {
                                ctx.Mark("клик по кнопке подтверждения заклинания");
                                W("EVENT заклинание шаг 2: подтверждение");
                                if (Click(abil.Obj_ButtonAbilityCh_Decision, "подтвердить заклинание")) { spellPicks++; spellStep = 0; abilitySince = now + 1; }
                            }
                        });
                    }
                }
                else if (!V_Ability_Manager.Ability_SelectON) { abilitySince = -1; spellStep = 0; }

                // тестовый режим: сами открываем предложение артефакта (без ожидания сундука)
                double every = ctx.Setting("forceoffer");
                if (every > 0 && !OfferOpen() && !V_Ability_Manager.Ability_SelectON && t > 15 && now - lastForce > every)
                {
                    lastForce = now;
                    ctx.Guard("Create_Artifact", () =>
                    {
                        if (art == null) art = UnityEngine.Object.FindObjectOfType(Il2CppInterop.Runtime.Il2CppType.Of<V_Artifact_Manager>())?.TryCast<V_Artifact_Manager>();
                        if (art == null) { W("EVENT не нашёл V_Artifact_Manager"); return; }
                        W("EVENT вызываю Create_Artifact (принудительно)");
                        ctx.Mark("Create_Artifact (бот)");
                        art.Create_Artifact();
                    });
                }

                // выбор артефакта из сундука
                bool offer = OfferOpen();
                if (offer && ctx.Setting("pick") > 0)
                {
                    if (offerSince < 0) { offerSince = now; countBefore = ObtainCount(); W($"EVENT окно артефактов открыто, тип {Z_Database.ArtifactType}, в списке {countBefore}"); }
                    // два шага с паузой: клик по карточке, через 1,5 с — по кнопке «получить»; при ошибке пишем, на каком шаге и в каком состоянии окно
                    if (pickedAt < 0 && now - offerSince > 2 && now - lastTry > 1.5)
                    {
                        lastTry = now;
                        ctx.Guard("выбор артефакта", () =>
                        {
                            string State() { try { return $"SelectArtifactID={V_Artifact_Manager.SelectArtifactID} ApplyArtifactSheetID={art.ApplyArtifactSheetID} ArtifactSelectNum={art.ArtifactSelectNum}"; } catch { return "состояние недоступно"; } }
                            if (pickStep == 0)
                            {
                                var sheets = art.List_ObjArtifactSelectSheet;
                                int idx = -1;
                                for (int i = 0; i < art.List_ArtifactSelectID.Count; i++) if (art.List_ArtifactSelectID[i] > 0) { idx = i; break; }
                                if (idx < 0) { W("EVENT в предложении нет артефактов"); return; }
                                if (!_dumpedButtons)
                                {
                                    _dumpedButtons = true;
                                    DumpButtons(sheets[idx], "карточка");
                                    DumpButtons(art.Obj_Button_ArtifactSelect, "получить");
                                }
                                W($"EVENT шаг 1: клик по карточке {idx} (артефакт {art.List_ArtifactSelectID[idx]}); {State()}");
                                ctx.Mark($"клик по карточке {idx}");
                                try { if (!Click(sheets[idx], "карточка")) return; }
                                catch (Exception e) { W($"EVENT шаг 1 упал: {FirstLine(e)}; {State()}"); throw; }
                                pickStep = 1; stepAt = now;
                                W($"EVENT шаг 1 выполнен; {State()}");
                            }
                            else if (now - stepAt > 1.5)
                            {
                                W($"EVENT шаг 2: клик по «получить»; {State()}");
                                ctx.Mark("клик по кнопке «получить»");
                                try { if (!Click(art.Obj_Button_ArtifactSelect, "получить")) return; }
                                catch (Exception e) { W($"EVENT шаг 2 упал: {FirstLine(e)}; {State()}"); throw; }
                                pickedAt = now;
                                W($"EVENT шаг 2 выполнен; {State()}");
                            }
                        });
                        if (pickedAt < 0 && ++failedPicks > 8) Finish("FAIL", "не удалось выбрать артефакт");
                    }
                }
                else if (!offer && offerSince >= 0)
                {
                    // окно закрылось: проверяем, что артефакт получен
                    int after = ObtainCount();
                    bool ok = after > countBefore;
                    picks++;
                    W($"CHECK артефакт получен (было {countBefore}, стало {after}) {(ok ? "PASS" : "FAIL")}");
                    offerSince = -1; pickedAt = -1; pickStep = 0;
                    ctx.Guard("проверка синергий", () => CheckSynergies());
                    if (!ok) Finish("FAIL", "артефакт не получен после выбора");
                    else if (ctx.Setting("picks") > 0 && picks >= ctx.Setting("picks")) Finish("PASS", $"выбрано артефактов: {picks}");
                }

                if (now - lastHb >= 5)
                {
                    lastHb = now;
                    string hp = "?";
                    try { hp = C_Battle_AllyUnit.Unit_IsLive ? "live" : "dead"; } catch { /* */ }
                    W($"HB t={t:0} hero={hp} artifacts={ObtainCount()} picks={picks} spells={spellPicks} цель={navTarget} поз=({h.transform.position.x:0.0};{h.transform.position.y:0.0};{h.transform.position.z:0.0}) стик=({stick.x:0.0};{stick.y:0.0};{stick.z:0.0})");
                }
                if (now - lastStat >= 10)
                {
                    lastStat = now;
                    ctx.Guard("снимок полей", () =>
                    {
                        foreach (var kv in props)
                            if (kv.Value != null) W($"STAT {kv.Key}={Convert.ToDouble(kv.Value.GetValue(h)):0.##}");
                        var area = h.Transform_ObjItemObtainArea;
                        if (area != null) W($"STAT ObtainAreaScaleX={area.localScale.x:0.##}");
                    });
                }
                try
                {
                    if (t > 10 && !C_Battle_AllyUnit.Unit_IsLive) { if (++deadTicks >= 15) Finish("FAIL", "герой погиб"); }
                    else deadTicks = 0;
                }
                catch { /* */ }
                if (t >= ctx.Setting("duration")) Finish("PASS", $"прошло {t:0} с, артефактов выбрано {picks}");
            });
        }
    }
}
