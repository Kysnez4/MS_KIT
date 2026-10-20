# Активные заклинания: как игра запускает магию и можно ли вызвать её из мода

Разведка (2026-10-18), код не менялся. Версия 612, `libil2cpp.so` arm64. RVA — из `modkit/dump.cs`; адрес в Ghidra = RVA + 0x100000.
Пометки: [П] подтверждено чтением дизассемблера, [Д] догадка / не проверено на устройстве.

## 0. Как делалась разведка (и что не вышло)
GhidraMCP запущен в фоне (`start_ghidra.ps1`), порт 8081 отвечает на `initialize`, но **ни один инструмент не отвечал** (`get_program_info` висел 20+ минут, python-процесс сервера грузил CPU, потом я его остановил). Инструменты `mcp__ghidra__*` в сессии так и не появились. Поэтому разбор сделан **своим дизассемблером**: Python + `capstone` (установлен в папку сессии, не в проект) читает `D:\tools\ghidra\targets\libil2cpp.so` напрямую (сегменты ELF = RVA), ищет вызовы `bl` и обращения `ldr/str [reg,#смещение]`, имена берутся из `dump.cs`. Декомпилятора нет: выводы — по ассемблеру. Скрипты лежат только в scratchpad сессии.
Совет: если Ghidra ещё нужна, при первом запуске дать ей отработать (анализ долгий), а не слать запросы в очередь.

## 1. Корутины заклинаний (всё в `C_Battle_AllyUnit`)
Метод `Play_X()` (возвращает `IEnumerator`) создаёт объект `<Play_X>d__N`, у которого `<>4__this` (+0x20) = герой, `<>1__state` (+0x10). Тело — `MoveNext`.

| Заклинание | ID (индекс `List_AllyMagicLevel`) | Флаг «запущено» | `Play_X` (RVA) | `MoveNext` (RVA) | Класс |
|---|---|---|---|---|---|
| Магическая пуля | 1 | `IsMagicBullet_P` +0xf35 | — (не разбирал) | — | |
| Огненный шар | 2 | `IsFireball_P` +0xf1c | `Play_Fireball` 0x104d638 | 0x106bb78 | `<Play_Fireball>d__1134` |
| Ледяные иглы | 5 | `IsIceNova_P` +0xf25 | `Play_IceNova` 0x104ee94 | 0x1073704 | `d__1169` |
| Молния | 7 | `IsThunderA_P` +0xf1d | `Play_ThunderA` 0x104ecf4 | 0x1084340 | `d__1165` |
| Электрозона | 8 | `IsElectricZone_P` +0xf21 | — | — | |
| Цунами | 9 | `IsTsunami_P` +0xf20 | — | — | |
| Метеорит | 10 | `IsMeteor_P` +0xf22 | `Play_Meteor` 0x104c78c | 0x10773e8 | `d__1098` |
| Вихрь / Электр. удар / Испепеление / Энерг. пуля / Метель / Луч / Лава / Вспышка | 12/13/15/16/17/18/20/21 | `IsTornado_P` … `IsFlashShock_P` | `Play_*` | не разбирал | |
| Магич. круг / Армагеддон | 19 / 14 | `IsMagicCircle_P` / `IsArmageddon_P` | | | |

[П] ID получены разбором `StartInformation_B`: перед проверкой флага стоит `mov w1,#ID; List<int>.get_Item` по `List_AllyMagicLevel`. Остальные ID (3,4,6,11 — дух, спутник, щит, призрак и т. д.) не расшифровывал.
Комбо-варианты: `Play_Fireball_Com01` 0x104d708, `_Com03` 0x104d770; `Play_Meteor_Com01/Com03` 0x104c7f4/0x104c85c; `Play_Thunder_Com02/03`, `Play_ThunderC` (0x104ed5c, MoveNext 0x1084c64) запускаются при активных комбинациях `MagicCom`. Обычный `Play_Fireball` при этом останавливается (`StopCoroutine`), см. `IsThunderC_P`, `IsWildShot_P` в `StartInformation_B`.

## 2. Кто запускает [П]
`C_Battle_AllyUnit.StartInformation_B` (RVA 0x11c0684, огромная; в ней же `ApplySatelliteGroup` и записи `UnitApply_*Num`) для каждого заклинания содержит блок одной формы (шаг между блоками 0x8c байт, начало блока Огненного шара 0x11cdad0):

```
lvl = Z_Database.List_AllyMagicLevel[ID]
если lvl >= 1 и флаг IsX_P == false: IsX_P = true;  this.StartCoroutine("Play_X")      // MonoBehaviour.StartCoroutine(string) 0x2196770
если lvl == 0 и флаг IsX_P == true : IsX_P = false; this.StopCoroutine("Play_X")      // 0x2196cb4
```
Запуск идёт **по имени строки** (поэтому у `Play_X` нет ни одного прямого `bl` в бинаре — только указатель в таблице `MethodInfo`: Play_Fireball 0x431730, Play_Meteor 0x4313d0). Для Метеорита блок начинается около 0x11cdd50 (запись `IsMeteor_P` в 0x11cdd50+). `StartInformation_B` вызывается при выборе/повышении заклинания (и в начале боя), поэтому корутина стартует один раз и живёт, пока `lvl>0`.
Дальше корутина — **бесконечный цикл**, сама ждёт `WaitForSeconds` между залпами. Отдельного менеджера, который бы «давал команду на каст», нет: `C_Battle_Manager` корутины заклинаний не запускает (он запускает только свои, `Start`, `End_Battle`, `Create_UnitEnemy_*`).

## 3. Что делает корутина (на примере Огненного шара, 0x106bb78) [П]
Состояния: 0 — инициализация, 1/2/3 — ожидание, затем поиск цели и залп, 4 — служебное.
1. Состояние 0: `<ApplyDamageRatio>=1`, `s8 = UnitApply_FireballPSpeed` (+0xbec), `<Transform_ObjPGUnit> = hero.Transform_ObjUnit` (+0x3b8), создаются списки `List<float>` расстояний и `List<int>` врагов; `yield new WaitForSeconds(PSpeed)`, state=1.
2. Состояние 1/2/3: из статических списков боя (`C_Battle_Manager`, получение через два `bl` 0x1248fe0/0x1255c98 — [Д] список врагов) строит расстояния от героя (`Transform.get_position`), берёт ближайшего (`List<int>` индексы врагов), сравнивает `расстояние + размер… <= UnitApply_ShootingRange` (+0xab4). Если не достаёт — `yield WaitForSeconds(короткая константа)`, state=2 (повтор).
3. Если достаёт: цикл `i = 0..round(UnitApply_FireballNum)` (+0xbe0) **без пауз внутри одного MoveNext**: проигрывается звук (`V_SoundManager.PlaySound`, один раз при i==1), и для каждого снаряда вызывается
   **`C_Battle_AllyUnit.Bullet_Create(Transform Transform_CreateUnit, int BulletID, float EGUnit_X, float EGUnit_Y, float EGUnit_Z, float AccuracyAimPlus, float PowerVar01, float PowerVar02, float SpecialVar01..04, float DamageRatio)`** — RVA **0x1037eb0**, `BulletID = hero.BulletID_Fireball` (+0xfb4), цель — координаты ближайшего врага (X,Y,Z), `AccuracyAimPlus = i * const` (веер), `DamageRatio` из `<ApplyDamageRatio>`.
   `UnitApply_FireballCastPowerNum` (+0xbf4) — какой по счёту снаряд усиленный (множитель 1.5 у `i == CastPowerNum`).
4. После залпа: счётчик `ApplyCooltimeEditCastNum` (по `static ApplyCooltimeEditCastNumCondition`/`Inc`) — каждый N-й залп на половину короче ожидание и умноженный урон (`Synergy08_Var`); затем **`yield WaitForSeconds(UnitApply_FireballPSpeed)`**, state=3.

**Цель/позиция:** ближайший враг по расстоянию до `Transform_ObjUnit` героя; своей «точки прицеливания» нет — Bullet_Create получает мировые X,Y,Z.
**Метеорит** (0x10773e8): позиции берёт из `List<int> <List_ApplyLocation>` (случайные точки через `UnityEngine.Random.Range`), число за залп — **статическое** `UnitApply_MeteorNum` (static +0x184, читается каждый виток цикла), `UnitApply_MeteorCastPowerNum` (+0xcac, усиленный N-й), `MeteorAddCastRan`/`IsMeteorAddCast` (дополнительный каст), урон падения — `C_Battle_AllyMeteorLand` (поля `ApplyMeteorLand_*`, `UnitApply_MeteorLandDamage/Size/Time`). Два `Bullet_Create` (основной и «доп. каст»).
**Молния ThunderA** (0x1084340): число ударов `UnitApply_ThunderNum` (+0xc08) копируется в поле итератора `<Apply_ThunderNum>` (+0x2c) в начале витка; `UnitApply_ThunderPSpeed` — пауза; цели — те же списки врагов; один `Bullet_Create` в цикле; `UnitApply_ThunderDoubleCastNum`, `ThunderArea`.
Читается из героя: `UnitApply_*Damage` (через `Bullet_Create`/`C_Battle_Bullet` — урон пули считается там [Д]), `*Num`, `*PSpeed`, `*Area`, `*Size`, `CriRan`, `ShootingRange`, `BulletID_*`. Из `UnitApply_*` вне героя ничего: «UnitApply_*» — это поля самого `C_Battle_AllyUnit` (индексы из `Z_Database.AllyUnitVar_*` попадают сюда в `AbilityStatApply`).

## 4. Можно ли вызвать из C#-мода
Да, тремя способами (по убыванию надёжности). Все вызовы — на главном потоке Unity, герой — `C_Battle_AllyUnit` (тот же, у которого уже видны `UnitApply_*`).

**A. Низкоуровневый — `Bullet_Create`** (0x1037eb0): `hero.Bullet_Create(hero.Transform_ObjUnit, hero.BulletID_Fireball, x, y, z, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f)` создаёт снаряд, остальное (урон, крит, эффекты) считает сам снаряд по полям героя. Нет побочных эффектов цикла (звук, счётчик каста, синергии, `CastPowerNum`) — их повторить руками. Самый предсказуемый способ «по своему триггеру выпустить N снарядов в точку». Параметры `PowerVar01/02`, `SpecialVar01..04` для Огненного шара из логов видно по нулям, у других заклинаний значимые — надо смотреть значения в `Play_X` для каждого (в Fireball: PowerVar01=1.0 или 1.5 для усиленного, остальное 0, DamageRatio = ApplyDamageRatio).
**B. Корутинный — «один залп вручную»**: `var it = hero.Play_Fireball();` (возвращает `IEnumerator`), затем `it.MoveNext(); it.MoveNext();` — первый вызов выполняет инициализацию (state 0 → ждёт), второй — сразу поиск цели и залп (ожидания `WaitForSeconds` игнорируем, это только значения `Current`). Если цель вне `ShootingRange`, залпа не будет (state=2); проверить можно по `it.Current` (WaitForSeconds) — но проще повторить. Полный цикл/пауз не запускать. Тайминг перезарядки при этом не затрагивается вовсе: «перезарядка» — только `WaitForSeconds` внутри самой корутины. [Д] проверить на устройстве, что второй `MoveNext` без паузы не требует `Time.time`-состояния (читал дизассемблер, в нём нет обращений к времени, кроме создания WaitForSeconds).
**C. Через движок Unity** — `hero.StartCoroutine(hero.Play_Fireball())` запустит ещё один бесконечный цикл (удвоит каст): подходит только для «временного» заклинания с `StopCoroutine` по ссылке `Coroutine`.
Сигнатуры `Play_*`: без аргументов. `Play_Doppelganger_Motion(SpriteRenderer)` — единственная с аргументом.
Побочные эффекты: звук (`V_SoundManager.PlaySound`, 1 раз при i==1), `ApplyCooltimeEditCastNum` (только внутри итератора, при пересоздании сбрасывается в 0), для Метеорита создаются `C_Battle_AllyMeteorLand`, и у корутин при каждом запуске — запись `strb 1, [x20,#0xca]`/`0xeb` (какие-то флаги героя; для Метеорита 0xeb) — [Д] смысл не выяснен.

## 5. Где перезарядка и как отключить авто-каст
**Перезарядка — не поле, а `WaitForSeconds(UnitApply_<Магия>PSpeed)` в конце залпа** (+0xbec для Огненного шара; Meteor +0xca8, Thunder +0xc10). Её масштабирует `UnitApply_MagicCooltimeRatio` (+0xac0): читается в `AbilityStatApply` (1 раз) и `StartInformation_B` (27 раз) при пересчёте — результат кладётся в `UnitApply_*PSpeed` [П по поиску `ldr s,[x,#0xac0]`]. Корутина читает PSpeed в конце каждого залпа, поэтому изменение действует на следующую паузу. «Индикатора готовности» нет.
Отключение авто-каста (любой способ рабочий):
1. **До получения заклинания**: поставить `IsFireball_P = true` (+0xf1c). `StartInformation_B` увидит флаг и не будет запускать корутину (при `lvl>=1 и флаг==true` — ничего не делает). Нужно выставлять заново после каждого входа в бой (поля героя сбрасываются/создаются заново). Если заклинание потом пропадёт (lvl==0), игра сама снимет флаг и вызовет `StopCoroutine`.
2. **Остановить уже работающую**: `hero.StopCoroutine("Play_Fireball")` (перегрузка по строке работает, потому что запуск был по строке) при том что флаг оставить `true` — иначе `StartInformation_B` при следующем пересчёте запустит заново. [Д] не проверено, что `StopCoroutine(string)` в Il2CppInterop вызывается без падения — проверить сначала на Fireball.
3. **Подменить `Play_X`**: Harmony Prefix на `C_Battle_AllyUnit.Play_Fireball` с `__result = пустой IEnumerator` — игра запустит «пустую» корутину и ничего не сделает; флаг остаётся `true`. Чище по логике, но Harmony с `__result` на методах, возвращающих IEnumerator, ранее обнулял поля (см. MELONLOADER.md раздел 11) — проверять.
4. Увеличить `UnitApply_FireballPSpeed` до огромного значения нельзя: PSpeed используют ещё `Play_IsConstellation` и др. (читают тот же +0xbec).
Для Магической пули и других с несколькими корутинами (`Com01/Com03`) надо блокировать и комбо-версии: они стартуют из блоков с `IsWildShot_P`, `IsThunderC_P` и т. д. (`StartInformation_B`).

## 6. Лимит снарядов v2 — читается ли число внутри корутины
**Да, каждая корутина читает число прямо из поля героя внутри своего `MoveNext`, без отдельной функции-геттера** [П]:
- Огненный шар: `ldr s0,[x20,#0xbe0]` (UnitApply_FireballNum) в 0x106c1d4, условие продолжения цикла `if (FireballNum >= i+1)`, перечитывается на каждом снаряде. Весь залп (все i) — внутри одного вызова `MoveNext`.
- Метеорит: статическое `UnitApply_MeteorNum` читается каждый виток цикла в 0x10787fc / 0x1078830 (статик `+0x184`), число за залп — внутри одного `MoveNext`; отдельно `UnitApply_MeteorCastPowerNum`.
- Молния ThunderA: `UnitApply_ThunderNum` (+0xc08) читается один раз в 0x10844dc (state 0 нового витка) и кладётся в `<Apply_ThunderNum>` (+0x2c итератора, float).
- Электр. удар: у итератора поле `<ApplyElectricNum>` (int) — копия (читается тоже в начале витка) [Д].
То есть для хука (Harmony) нет «функции получения числа», но есть удобная точка — **сам `MoveNext` итератора заклинания**:
- **Prefix** на `MoveNext` класса `<Play_Fireball>d__1134` (RVA 0x106bb78): взять `this.<>4__this` (герой), запомнить настоящее `UnitApply_FireballNum`, записать `min(real, cap)`; **Postfix**: вернуть настоящее значение. За время одного вызова корутина создаст не больше `cap` снарядов, а расчёт круга/статы/окно характеристик увидят настоящее число. Тот же приём для Метеорита (статическое `UnitApply_MeteorNum`), ThunderA (поле итератора `<Apply_ThunderNum>` можно ещё и поправить в postfix: min(cap)) и т. д.
- Postfix с коррекцией бонуса урона: можно за один и тот же `MoveNext` умножить `<ApplyDamageRatio>` (+0x28) на бонус за «лишние» снаряды (то, что сейчас делает мод через `AbilityStatApply`).
- Таблица для хуков: Fireball 0x106bb78 (Num +0xbe0), IceNova 0x1073704 (число серии — не проверено), ThunderA 0x1084340 (+0xc08), Meteor 0x10773e8 (static +0x184), Electric 0x106787c. Остальные `Play_*` — по тому же шаблону: `grep "<Play_X>d__N" dump.cs` → `MoveNext` RVA.
Спутники/духи/щит (число влияет на раскладку) — НЕ в этих корутинах: их число читается в `ApplySatelliteGroup`/`Start_Satellite…`, это отдельное; для них хук на `MoveNext` не поможет.

## 7. Предложение API для мода
```csharp
// в ModKit.dll
public interface ISpells {
    // выпустить залп заклинания ID (1=пуля,2=огненный шар,7=молния,10=метеорит,…) вручную
    bool Cast(C_Battle_AllyUnit hero, int spellId, Vector3? target = null);   // B: it.MoveNext x2 или A: Bullet_Create
    void SetAuto(int spellId, bool enabled);          // IsX_P = !enabled до StartInformation_B; StopCoroutine при отключении
    event Action<int> SpellObtained;                  // lvl 0 -> >=1 (из List_AllyMagicLevel)
}
```
- `SpellTable`: {id → (флаг-смещение `IsX_P`, имя `Play_X`, `BulletID_*`, `UnitApply_*Num/PSpeed`, MoveNext RVA)} — строится по полям Il2CppInterop (`IsFireball_P` и т. д.), без RVA в коде.
- `Cast` по умолчанию: способ A (`Bullet_Create`) — самый простой для «Огненного шара»; способ B — когда нужны все эффекты игры (синергии, комбинации, `CastPowerNum`).
- Свой таймер перезарядки в моде (`ctx.Every`), с учётом `UnitApply_MagicCooltimeRatio`.
- Хук лимита (раздел 6): `ctx.Before/After("<Play_Fireball>d__1134:MoveNext", …)` — класс `Сборка:Класс` по текущему формату `Hooks.cs` (вложенные классы `<…>d__N` нужно проверить: Harmony находит их по `Il2Cpp.C_Battle_AllyUnit.__c__…`/`Il2Cpp.<Play_Fireball>d__1134`).

## 8. Риски и непроверенное
- **Ничего из этого не запускалось на устройстве.** Всё по ассемблеру: в частности «2×MoveNext = залп», «StopCoroutine(string) работает», Harmony-патч MoveNext итератора. Проверять по порядку: `Bullet_Create` (A) → два `MoveNext` (B) → `StopCoroutine`/`IsX_P` (отключение) → prefix/postfix на `MoveNext`.
- Harmony Prefix/Postfix на `MoveNext` итератора: метод вызывается каждый кадр/каждую паузу (десятки раз в секунду на заклинание), нужен очень лёгкий код; бывший опыт — массовые хуки роняли игру (Scudo), хук только на нужные 4–6 методов.
- `UnitApply_MeteorNum` — **статическое** поле класса `C_Battle_AllyUnit`: запись через статическое свойство Il2CppInterop; в других местах (AbilityStatApply) оно тоже пишется.
- Prefix на `MoveNext` итератора живёт на уровне *класса итератора* (`d__N`) — номер `N` меняется при обновлении игры (как и все RVA); привязывать по имени `Play_X` через `Play_X` → `<…>d__N` из `dump.cs` при сборке, не вручную.
- Ручной вызов `Bullet_Create` не проходит через счётчик `ApplyCooltimeEditCastNum` и синергии `Synergy08_Var`; усиленный (1.5) снаряд — только если сами передать `PowerVar01=1.5`.
- Сброс/пересоздание: `IsX_P` сбрасывается вместе с героем в новом бою; `StartInformation_B` вызывается при каждом выборе заклинания — флаг нужно ставить ПЕРЕД ним, иначе корутина уже стартовала.
- Ghidra не использована: возможны ошибки в именах полей, если смещение попало в чужой класс (мой аннотатор подставляет имена полей `C_Battle_AllyUnit` и для объектов других классов — смещения у итераторов и героя отличались, я сверял вручную по `<>4__this` и `+0x28/0x2c` и т. д.).
