using System;
using Maoshi.Cat;
using Maoshi.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Магазин аксессуаров (из главного меню). Слева — свой кот крупным планом (примерка сразу видна),
    /// справа — вкладки «Голова / Шея / Морда» и вещи с ценой в консервных банках.
    /// Нажал на вещь — кот примеряет её; кнопка снизу: «Купить за N», «Надеть» или «Снять».
    /// Банки и покупки хранятся на этом компьютере (Settings.Cosmetics) и переживают партии.
    /// </summary>
    public class ShopScreen
    {
        public VisualElement Root { get; }

        private readonly Action _close;
        private readonly VisualElement _preview;
        private readonly VisualElement _cans;
        private readonly VisualElement _tabs;
        private readonly VisualElement _list;
        private readonly Label _itemName;
        private readonly Label _itemState;
        private readonly Button _action;
        private AccessorySlot _slot = AccessorySlot.Head;
        private int _selected = -1;
        private bool _dirty = true;

        public ShopScreen(Action close)
        {
            _close = close;
            Root = UIKit.Layout("layer", "menu-backdrop", "shop");
            UIKit.Backdrop(Root);

            var content = UIKit.Layout("layer", "menu-root");
            Root.Add(content);

            var head = UIKit.Layout("shop-head");
            head.Add(UIKit.Text("Магазин", "title"));
            content.Add(head);

            var columns = UIKit.Layout("menu-columns");
            content.Add(columns);

            var card = UIKit.Box("panel", "skin-card", "shop-preview-card");
            _preview = UIKit.Box("skin-preview-image");
            card.Add(_preview);
            _itemName = UIKit.Text("", "skin-name");
            _itemState = UIKit.Text("", "skin-count");
            card.Add(_itemName);
            card.Add(_itemState);
            _action = UIKit.Button("", OnAction, "btn--primary");
            card.Add(_action);
            columns.Add(card);

            var panel = UIKit.Box("panel", "menu-form", "shop-panel");
            // Банки — в шапке панели с вещами, рядом с ценами.
            var wallet = UIKit.Layout("shop-wallet");
            wallet.Add(UIKit.Text("Твои банки", "shop-wallet__label"));
            wallet.Add(UIKit.Layout("grow"));
            _cans = UIKit.Layout("shop-cans");
            wallet.Add(_cans);
            panel.Add(wallet);
            _tabs = UIKit.Box("segmented");
            foreach (AccessorySlot slot in Enum.GetValues(typeof(AccessorySlot)))
            {
                var s = slot;
                var tab = new Button(() => { _slot = s; _selected = Cosmetics.Equipped(s); _dirty = true; }) { text = CatAccessories.SlotName(slot) };
                tab.AddToClassList("segment");
                tab.userData = slot;
                _tabs.Add(tab);
            }
            panel.Add(_tabs);
            // Вещей много — список прокручивается колёсиком.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("shop-scroll");
            _list = UIKit.Layout("shop-list");
            scroll.Add(_list);
            panel.Add(scroll);
            panel.Add(UIKit.Text("Банки: 3 за победу в партии и по 1 за каждую номинацию в финале.", "muted", "shop-hint"));
            var back = UIKit.Button("Назад", () => _close?.Invoke(), "btn--ghost");
            back.style.marginTop = 10;
            panel.Add(back);
            columns.Add(panel);

            Cosmetics.Changed += () => _dirty = true;
        }

        /// <summary>Открыли магазин: начать с надетой вещи на голове.</summary>
        public void Open()
        {
            _slot = AccessorySlot.Head;
            _selected = Cosmetics.Equipped(_slot);
            _dirty = true;
        }

        public void Refresh()
        {
            // Только в редакторе: F9 — +10 банок (проверить покупки).
            if (Application.isEditor && Maoshi.GameKeyboard.Current != null && Maoshi.GameKeyboard.Current.f9Key.wasPressedThisFrame)
                Cosmetics.DebugAdd(10);

            var preview = CatPreview.Get();
            if (preview != null)
            {
                preview.Touch();
                preview.FocusHead(true);
                int skin = GameSettings.CatSkin;
                preview.SetSkin(skin >= 0 ? skin : 0);
                // Примерка: в текущем слоте — выбранная вещь, в остальных — надетые.
                int head = _slot == AccessorySlot.Head ? _selected : Cosmetics.Equipped(AccessorySlot.Head);
                int neck = _slot == AccessorySlot.Neck ? _selected : Cosmetics.Equipped(AccessorySlot.Neck);
                int face = _slot == AccessorySlot.Face ? _selected : Cosmetics.Equipped(AccessorySlot.Face);
                preview.SetAccessories(head, neck, face);
                if (_preview.style.backgroundImage.value.renderTexture != preview.Texture)
                    _preview.style.backgroundImage = Background.FromRenderTexture(preview.Texture);
            }

            if (!_dirty) return;
            _dirty = false;

            _cans.Clear();
            _cans.Add(UIKit.Cans(Cosmetics.Cans, 30f, "shop-cans__value"));

            foreach (var child in _tabs.Children())
                child.EnableInClassList("segment--active", child.userData is AccessorySlot s && s == _slot);

            _list.Clear();
            for (int i = 0; i < CatAccessories.All.Length; i++)
            {
                var info = CatAccessories.All[i];
                if (info.Slot != _slot) continue;
                int index = i;
                var row = new Button(() => { _selected = index; _dirty = true; });
                row.AddToClassList("shop-item");
                if (index == _selected) row.AddToClassList("shop-item--selected");
                var swatch = UIKit.Swatch(info.Swatch);
                row.Add(swatch);
                row.Add(UIKit.Text(info.Name, "shop-item__name", "grow"));
                if (Cosmetics.Equipped(_slot) == index) row.Add(UIKit.Text("надето", "shop-item__tag", "shop-item__tag--on"));
                else if (Cosmetics.Owns(index)) row.Add(UIKit.Text("куплено", "shop-item__tag"));
                else row.Add(UIKit.Cans(info.Price, 22f, "shop-item__price"));
                _list.Add(row);
            }
            // «Без аксессуара» в конце списка.
            var none = new Button(() => { _selected = -1; _dirty = true; });
            none.AddToClassList("shop-item");
            if (_selected < 0) none.AddToClassList("shop-item--selected");
            none.Add(UIKit.Text("Без " + (_slot == AccessorySlot.Head ? "шапки" : _slot == AccessorySlot.Neck ? "украшения" : "очков"), "shop-item__name", "grow"));
            _list.Add(none);

            // Карточка выбранного.
            if (_selected < 0)
            {
                UIKit.SetText(_itemName, "Без аксессуара");
                UIKit.SetText(_itemState, CatAccessories.SlotName(_slot));
                bool anyOn = Cosmetics.Equipped(_slot) >= 0;
                _action.text = anyOn ? "Снять" : "Ничего не надето";
                _action.SetEnabled(anyOn);
                return;
            }

            var item = CatAccessories.All[_selected];
            UIKit.SetText(_itemName, item.Name);
            if (Cosmetics.Equipped(_slot) == _selected)
            {
                UIKit.SetText(_itemState, "надето");
                _action.text = "Снять";
                _action.SetEnabled(true);
            }
            else if (Cosmetics.Owns(_selected))
            {
                UIKit.SetText(_itemState, "куплено");
                _action.text = "Надеть";
                _action.SetEnabled(true);
            }
            else
            {
                bool enough = Cosmetics.Cans >= item.Price;
                UIKit.SetText(_itemState, enough ? "примерка" : $"не хватает {item.Price - Cosmetics.Cans} банок");
                _action.text = $"Купить за {item.Price} банок";
                _action.SetEnabled(enough);
            }
        }

        private void OnAction()
        {
            if (_selected < 0)
            {
                Cosmetics.Equip(_slot, -1);
                return;
            }
            if (Cosmetics.Equipped(_slot) == _selected)
            {
                Cosmetics.Equip(_slot, -1);
                return;
            }
            if (!Cosmetics.Owns(_selected))
            {
                if (!Cosmetics.TryBuy(_selected)) return;
                Maoshi.Audio.AudioManager.Play("fanfare", 0.6f);
            }
            Cosmetics.Equip(_slot, _selected);
            Maoshi.Audio.AudioManager.Play("ui_confirm");
        }

        /// <summary>Закрыли магазин — витрина снова показывает кота целиком с надетыми вещами.</summary>
        public void OnClosed()
        {
            var preview = CatPreview.Get();
            if (preview == null) return;
            preview.FocusHead(false);
            preview.SetAccessories(Cosmetics.Equipped(AccessorySlot.Head), Cosmetics.Equipped(AccessorySlot.Neck), Cosmetics.Equipped(AccessorySlot.Face));
        }
    }
}
