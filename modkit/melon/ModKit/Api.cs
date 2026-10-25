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
        /// <summary>API другого включённого мода (то, что он отдал через <see cref="Expose"/>); null — мода нет или он выключен. Для библиотек: <see cref="ModContextExt.Use{T}"/>.</summary>
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

        /// <summary>Короткая подпись на экране на sec секунд (по игровому времени).</summary>
        void Toast(string text, double sec = 3);
        /// <summary>Файл из zip этого мода (путь внутри архива, например "icons/a.png"); null — нет файла или мод не из zip.</summary>
        byte[] ReadAsset(string path);
        /// <summary>Код языка игры: ko, en, ja, zh_cn, zh_tw, es, de, pt, ru.</summary>
        string Lang { get; }
        /// <summary>Перевод из lang/&lt;язык&gt;.json в zip мода: язык игры → en → ru → fallback (если не задан — сам ключ).</summary>
        string T(string key, string fallback = null);
        /// <summary>Таймеры по игровому времени (на паузе стоят).</summary>
        IGameTimer Timer { get; }
        /// <summary>Постоянное хранилище мода (JSON).</summary>
        IStore Store { get; }
        /// <summary>Общие теги заклинаний, артефактов, врагов.</summary>
        ITags Tags { get; }

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

namespace ModKit
{
    /// <summary>
    /// Библиотека, отдающая свой API по моду-вызывающему: <see cref="For"/> получает ModContext, и всё, что библиотека меняет (хуки, правки, таймеры),
    /// откатывается при выключении именно этого мода. Библиотека публикует себя так: ctx.Expose(this) в OnEnable.
    /// </summary>
    public interface ILibrary<T> where T : class
    {
        T For(ModContext ctx);
    }

    public static class ModContextExt
    {
        /// <summary>
        /// API библиотеки для этого мода: <c>var combat = ctx.Use&lt;ICombat&gt;("modkit_combat");</c>. Повторный вызов возвращает тот же объект.
        /// Бросает исключение, если библиотека не включена (добавьте её в depends мода).
        /// </summary>
        public static T Use<T>(this ModContext ctx, string libraryId) where T : class
        {
            var key = (ctx.Id, libraryId, typeof(T));
            lock (UseCache)
            {
                if (UseCache.TryGetValue(key, out var cached)) return (T)cached;
                var lib = ctx.Mod<ILibrary<T>>(libraryId) ?? throw new InvalidOperationException($"библиотека «{libraryId}» не включена: добавьте её в depends мода {ctx.Id}");
                var api = lib.For(ctx);
                UseCache[key] = api;
                ctx.OnCleanup(() => { lock (UseCache) UseCache.Remove(key); });
                return api;
            }
        }

        /// <summary>Объекты библиотек по (мод, библиотека, тип): один экземпляр на мод, пока он включён (внутри — хуки, таймеры и состояние мода).</summary>
        private static readonly Dictionary<(string, string, Type), object> UseCache = new Dictionary<(string, string, Type), object>();

        /// <summary>
        /// Список отката, привязанный к моду: всё, что в него добавлено, выполняется (в обратном порядке) при выключении мода.
        /// Для библиотек, чьи внутренние классы принимают List&lt;Action&gt; cleanups.
        /// </summary>
        public static List<Action> CleanupList(this ModContext ctx)
        {
            var list = new List<Action>();
            ctx.OnCleanup(() =>
            {
                var items = new List<Action>(list);
                list.Clear();
                items.Reverse();
                foreach (var a in items)
                {
                    try { a(); } catch (Exception e) { ctx.Log("ошибка отката: " + e.Message); }
                }
            });
            return list;
        }
    }
}
