using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Короткие «кирпичики» интерфейса: панели, надписи, кнопки, строки. Внешний вид — в Maoshi.uss.
    /// Всё строится кодом, без UXML: так экраны легко менять и они не ломаются от переименований.
    /// </summary>
    public static class UIKit
    {
        public static VisualElement Box(params string[] classes)
        {
            var element = new VisualElement();
            foreach (var c in classes) element.AddToClassList(c);
            // Каждая панель — «наклейка» с жёсткой тенью.
            if (element.ClassListContains("panel") && !element.ClassListContains("panel--brawl"))
                StickerShadow.Attach(element, element.ClassListContains("panel--hud") ? 6f : 9f);
            return element;
        }

        // ───────── Картинки интерфейса (Resources/Maoshi/UI) ─────────

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> Images = new();

        public static Texture2D Image(string name)
        {
            if (Images.TryGetValue(name, out var cached) && cached != null) return cached;
            var texture = Resources.Load<Texture2D>("Maoshi/UI/" + name);
            Images[name] = texture;
            return texture;
        }

        /// <summary>Элемент-картинка из Resources/Maoshi/UI.</summary>
        public static VisualElement Picture(string name, params string[] classes)
        {
            var element = Layout(classes);
            var texture = Image(name);
            if (texture != null) element.style.backgroundImage = texture;
            return element;
        }

        /// <summary>Фон меню: вечерний двор, затемнение и меловые рисунки.</summary>
        public static void Backdrop(VisualElement root)
        {
            var image = Image("MenuBackdrop");
            if (image != null) root.style.backgroundImage = image;
            root.Add(Layout("menu-backdrop__shade"));
            root.Add(Picture("ChalkMenu", "chalk-layer"));
        }

        /// <summary>Подсказка «клавиша — действие».</summary>
        public static VisualElement Key(string key, string action)
        {
            var item = Layout("key-item");
            item.Add(Text(key, "keycap"));
            item.Add(Text(action, "key-label"));
            return item;
        }

        /// <summary>Контейнер, сквозь который проходят клики (для раскладки, а не для панелей).</summary>
        public static VisualElement Layout(params string[] classes)
        {
            var element = Box(classes);
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("btn");
            foreach (var c in classes) button.AddToClassList(c);
            return button;
        }

        public static VisualElement Row(params string[] classes)
        {
            var row = Box("row");
            foreach (var c in classes) row.AddToClassList(c);
            return row;
        }

        public static VisualElement Spacer(float height = 14f)
        {
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.height = height;
            return spacer;
        }

        private static Texture2D _fishIcon;

        /// <summary>Иконка валюты — рыбка (Resources/Maoshi/FishIcon).</summary>
        public static Texture2D FishIcon => _fishIcon != null ? _fishIcon : _fishIcon = Resources.Load<Texture2D>("Maoshi/FishIcon");

        /// <summary>Картинка-рыбка нужного размера.</summary>
        public static VisualElement FishImage(float size = 26f)
        {
            var image = Box("fish-icon");
            image.pickingMode = PickingMode.Ignore;
            image.style.width = size;
            image.style.height = size;
            if (FishIcon != null) image.style.backgroundImage = FishIcon;
            return image;
        }

        /// <summary>Количество рыбы: «12 🐟» (число и иконка в строку).</summary>
        public static VisualElement Fish(string amount, float iconSize = 26f, params string[] classes)
        {
            var row = Layout("fish-amount");
            var label = Text(amount, classes.Length > 0 ? classes : new[] { "text", "accent" });
            row.Add(label);
            row.Add(FishImage(iconSize));
            return row;
        }

        public static VisualElement Fish(int amount, float iconSize = 26f, params string[] classes) =>
            Fish(amount.ToString(), iconSize, classes);

        /// <summary>Консервные банки (валюта магазина): «12 🥫».</summary>
        public static VisualElement Cans(int amount, float iconSize = 26f, params string[] classes)
        {
            var row = Layout("fish-amount");
            row.Add(Text(amount.ToString(), classes.Length > 0 ? classes : new[] { "text", "accent" }));
            row.Add(CanImage(iconSize));
            return row;
        }

        public static VisualElement CanImage(float size = 26f)
        {
            var image = Picture("CanIcon", "fish-icon");
            image.style.width = size;
            image.style.height = size;
            return image;
        }

        public static VisualElement Swatch(Color color)
        {
            var swatch = Box("swatch");
            swatch.pickingMode = PickingMode.Ignore;
            swatch.style.backgroundColor = color;
            return swatch;
        }

        public static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        /// <summary>Поменять текст, только если он другой (меньше перерисовок).</summary>
        public static void SetText(TextElement element, string text)
        {
            if (element != null && element.text != text) element.text = text;
        }
    }

    /// <summary>
    /// Переключатель «‹ значение ›» для настроек: стрелки листают варианты по кругу.
    /// Удобнее выпадающих списков с мышью, клавиатурой и геймпадом.
    /// </summary>
    public class Selector : VisualElement
    {
        private readonly Label _value;
        private readonly Func<string> _getText;
        private readonly Action<int> _step;

        public Selector(string title, Func<string> getText, Action<int> step)
        {
            _getText = getText;
            _step = step;

            AddToClassList("settings-row");
            Add(UIKit.Text(title, "settings-row__label"));

            var selector = UIKit.Box("selector");
            var left = new Button(() => Step(-1)) { text = "‹" };
            left.AddToClassList("selector__arrow");
            var right = new Button(() => Step(1)) { text = "›" };
            right.AddToClassList("selector__arrow");
            _value = UIKit.Text("", "selector__value");

            selector.Add(left);
            selector.Add(_value);
            selector.Add(right);
            Add(selector);

            Refresh();
        }

        private void Step(int direction)
        {
            _step(direction);
            Refresh();
        }

        public void Refresh() => UIKit.SetText(_value, _getText());
    }
}
