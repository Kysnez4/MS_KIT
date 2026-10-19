using System;
using System.Collections.Generic;

namespace ModKit
{
    /// <summary>Числовая настройка мода: в окне «Моды» показывается кнопками − / + и полем ввода.</summary>
    public class ModOption
    {
        public string Key;
        public string Label;
        public double Default;
        public double Min;
        public double Max;
        public double Step = 1;
        /// <summary>Не показывать в списке (значение всё равно сохраняется) — для модов с собственным экраном.</summary>
        public bool Hidden;
    }

    /// <summary>Данные из mod.json внешнего мода (картинки — сырые байты PNG/JPG).</summary>
    public class ModMeta
    {
        public string Version;
        public string Author;
        public byte[] Icon;
        public List<byte[]> Screenshots = new List<byte[]>();
    }

    /// <summary>Описание мода. Мод — это объект ModDef; внешний мод отдаёт его из <see cref="IKitMod.Define"/>.</summary>
    public class ModDef
    {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>Числа через точку, например "1.2.0".</summary>
        public string Version;
        /// <summary>Зависимости: id мода -> допустимые версии ("*", "&gt;=1.2", "&gt;=1.0 &lt;2.0", "1.2" = не ниже).</summary>
        public Dictionary<string, string> Depends = new Dictionary<string, string>();
        /// <summary>Мягкий порядок: если перечисленные моды включены, этот запускается после них.</summary>
        public List<string> LoadAfter = new List<string>();
        /// <summary>
        /// Библиотека: не показывается в списке модов, включается сама, когда включён мод, который от неё зависит (Depends),
        /// и выключается, когда зависимых не осталось. Содержит общий код и типы (API-DLL) для других модов.
        /// </summary>
        public bool Library;
        /// <summary>Дополнительные DLL из zip (имена файлов): загружаются до main и доступны другим модам по имени сборки.</summary>
        public List<string> Assemblies = new List<string>();
        public bool DefaultEnabled;
        /// <summary>Мод правит данные игры: полностью заработает после перезапуска (окно «Моды» покажет плашку).</summary>
        public bool RestartRequired;
        public List<ModOption> Options = new List<ModOption>();

        public Action<ModContext> OnEnable;
        /// <summary>Дополнительный откат. Хуки, Every и OnCleanup откатываются сами.</summary>
        public Action<ModContext> OnDisable;
        /// <summary>Главное меню построено (на главном потоке): мод может добавить свои элементы. Второй аргумент — A_MainMenuInformation.</summary>
        public Action<ModContext, object> OnMainMenu;
        /// <summary>Если задан, у включённого мода в окне появляется кнопка «Настройки».</summary>
        public Action<ModContext> OpenSettings;

        /// <summary>Заполняет загрузчик для модов из .zip.</summary>
        public string Source;
        public ModMeta Meta;
    }

    /// <summary>Точка входа внешнего мода: в DLL из .zip должен быть ровно один класс с этим интерфейсом и публичным конструктором без параметров.</summary>
    public interface IKitMod
    {
        ModDef Define();
    }

    public interface ModEvents
    {
        /// <summary>Подписка. Снимается сама при выключении мода; пока мод выключен, обработчик не вызывается.</summary>
        void On(string name, Action<object> fn);
        /// <summary>Рассылает событие всем включённым подписчикам (в том числе других модов).</summary>
        void Emit(string name, object payload = null);
        /// <summary>Типизированная подписка: событие определяется типом T (тип можно объявить в библиотеке и делить между модами).</summary>
        void On<T>(Action<T> fn);
        /// <summary>Рассылает типизированное событие всем подписчикам On&lt;T&gt;.</summary>
        void Emit<T>(T payload);
    }

    /// <summary>Исключение «конфликт модов»: мод остаётся включённым в настройках и ждёт, пока освободится метод.</summary>
    public class ModConflictException : Exception
    {
        public ModConflictException(string message) : base(message) { }
    }

    public interface ModContext
    {
        string Id { get; }
        /// <summary>Текущие значения настроек (сохраняются между запусками).</summary>
        IReadOnlyDictionary<string, double> Settings { get; }
        double Setting(string key);
        /// <summary>Меняет значение настройки и сохраняет, не перезапуская мод.</summary>
        void SetSetting(string key, double value);
        void Log(string msg);
        /// <summary>Предупреждение работающего мода: показывается в карточке мода в окне «Моды».</summary>
        void Warn(string msg);
        ModEvents Events { get; }
        /// <summary>Публикует API мода для других модов (читается через <see cref="Mod{T}"/>). Убирается при выключении.</summary>
        void Expose(object api);
        T Mod<T>(string id) where T : class;

        /// <summary>
        /// Полностью заменяет метод (оригинал не вызывается). Класс — имя из Assembly-CSharp или «Сборка:Класс».
        /// Для не-void методов <paramref name="impl"/> возвращает результат. Если метод уже заменил другой мод — ModConflictException.
        /// </summary>
        void Replace(string className, string method, int paramCount, Func<object, object[], object> impl);
        /// <summary>Вызывает fn после метода. self — this-объект (Il2Cpp-обёртка), args — аргументы.</summary>
        void After(string className, string method, int paramCount, Action<object, object[]> fn);
        void Before(string className, string method, int paramCount, Action<object, object[]> fn);
        /// <summary>Периодический вызов на главном потоке, пока мод включён.</summary>
        void Every(double ms, Action fn);
        /// <summary>Выполнить при выключении мода.</summary>
        void OnCleanup(Action fn);
        /// <summary>
        /// Общий реестр по имени (предметы, артефакты, команды...): один и тот же для всех модов; записи мода убираются при его выключении.
        /// Тип T должен быть общим (объявлен в библиотеке или в ModKit).
        /// </summary>
        IRegistry<T> Registry<T>(string name);

        /// <summary>Регистрирует свой эффект артефакта/комбинации (ID >= 10000). Снимается при выключении мода.</summary>
        void DefineEffect(EffectDef def);
        /// <summary>Эффекты мода без привязки к артефакту: пары (ID эффекта игры, значение). Пустой массив снимает.</summary>
        void SetGlobalEffects((int Id, double Value)[] pairs);
        /// <summary>Подключает встроенные эффекты 10001–10004 (урон пули %, перезарядка −%, снарядов +N, радиус сбора %).</summary>
        void UseBuiltinEffects();
        /// <summary>
        /// Проход по герою: fn вызывается после каждого пересчёта характеристик и раз в ~0,7 с, пока идёт бой. Через view.Field(...).Value
        /// читается значение игры (база), запись задаёт результат; поля, которых проход перестал касаться, возвращаются к базе.
        /// </summary>
        void OnHeroPass(Action<IHeroView> fn);
        /// <summary>Заклинания героя: уровень, отключение авто-каста, ручной каст. Авто-каст возвращается при выключении мода.</summary>
        ISpells Spells { get; }
        /// <summary>Состояние игры: класс, зона, время боя, артефакты, уровни заклинаний, практика классов. Событие battleResult приходит после End_Battle.</summary>
        IGame Game { get; }

        /// <summary>
        /// Выполняет fn под защитой: исключение C# не пробрасывается дальше, а пишется в журнал («ОШИБКА [мод] что: ...»).
        /// Серия ошибок мода (5 за 10 с) отключает мод, игра продолжает работать. Нативные сбои игры так не ловятся — для них есть <see cref="Mark"/>.
        /// </summary>
        void Guard(string what, Action fn);
        /// <summary>Сообщает об ошибке мода в журнал и в карточку мода (как Warn, но с пометкой ОШИБКА и подсчётом для автоотключения).</summary>
        void Error(string msg);
        /// <summary>
        /// Метка в журнале вылетов: «сейчас мод делает это». Если игра упадёт, при следующем запуске в modkit.log будет видно последнюю метку.
        /// Ставьте перед рискованными вызовами (запись в нативную память, правка таблиц). Дёшево, можно в горячем коде.
        /// </summary>
        void Mark(string what);
    }
}

namespace ModKit
{
    /// <summary>Общий реестр записей «id → значение». Изменения рассылаются событием <see cref="RegistryChanged"/>.</summary>
    public interface IRegistry<T>
    {
        string Name { get; }
        /// <summary>Добавляет запись от имени текущего мода (повторный id заменяет её). Запись уберётся при выключении мода.</summary>
        void Register(string id, T value);
        /// <summary>Убирает запись, добавленную этим модом.</summary>
        bool Unregister(string id);
        bool TryGet(string id, out T value);
        IReadOnlyDictionary<string, T> All { get; }
    }

    /// <summary>Событие (ModEvents.On&lt;RegistryChanged&gt;): в реестре добавлена или убрана запись.</summary>
    public class RegistryChanged
    {
        public string Registry, Id, Owner;
        public bool Added;
    }
}
