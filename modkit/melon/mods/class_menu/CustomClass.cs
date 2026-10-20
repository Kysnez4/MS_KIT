using System.Collections.Generic;

namespace ModKit
{
    /// <summary>
    /// Свой класс персонажа для меню «Классы мода» (мод class_menu). Регистрируется в общем реестре: ctx.Registry&lt;CustomClass&gt;("classes").Register(id, класс).
    /// Эффекты выбранного класса применяются движком эффектов ядра (подстановка настоящих эффектов игры, как у мода «Проклятия и дары»), а не таблицей Class игры.
    /// </summary>
    public class CustomClass
    {
        public string Name;
        /// <summary>Строки описания (показываются на странице класса).</summary>
        public List<string> Lines = new List<string>();
        /// <summary>Эффекты: (ID эффекта игры из docs/EFFECTS.md, значение). Отрицательный ID — заклинание №N получает +значение уровней (в бою не проверено).</summary>
        public List<(int Id, double Value)> Effects = new List<(int, double)>();
        /// <summary>Подпись под названием (необязательно), например автор.</summary>
        public string Subtitle;
        /// <summary>PNG/JPG иконки (необязательно): без неё на карте классов рисуется квадратик.</summary>
        public byte[] Icon;
    }
}
