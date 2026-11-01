using UnityEngine;
using UnityEngine.UIElements;

namespace Maoshi.UI
{
    /// <summary>
    /// Жёсткая тень «наклейки» под панелью: тёмная плашка того же размера и скругления, сдвинутая вправо-вниз.
    /// В UI Toolkit нет box-shadow, поэтому тень — отдельный элемент перед панелью в том же родителе.
    /// Следит за размером, положением, поворотом и видимостью панели; уходит вместе с ней.
    /// </summary>
    public static class StickerShadow
    {
        public static void Attach(VisualElement target, float offset)
        {
            var shadow = new VisualElement { name = "sticker-shadow", pickingMode = PickingMode.Ignore };
            shadow.AddToClassList("sticker-shadow");
            shadow.style.position = Position.Absolute;

            void Place()
            {
                var parent = target.parent;
                if (parent == null) return;
                if (shadow.parent != parent)
                {
                    shadow.RemoveFromHierarchy();
                    parent.Insert(parent.IndexOf(target), shadow);
                }
                else if (parent.IndexOf(shadow) != parent.IndexOf(target) - 1)
                {
                    shadow.PlaceBehind(target);
                }
                Sync();
            }

            void Sync()
            {
                if (shadow.parent == null || target.parent == null) return;
                var style = target.resolvedStyle;
                bool visible = style.display != DisplayStyle.None && style.visibility == Visibility.Visible && !float.IsNaN(target.layout.width);
                shadow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;

                // Абсолютное положение отсчитывается от внутреннего края рамки родителя.
                var parentStyle = target.parent.resolvedStyle;
                var rect = target.layout;
                shadow.style.left = rect.x - parentStyle.borderLeftWidth + offset;
                shadow.style.top = rect.y - parentStyle.borderTopWidth + offset;
                shadow.style.width = rect.width;
                shadow.style.height = rect.height;
                shadow.style.borderTopLeftRadius = style.borderTopLeftRadius;
                shadow.style.borderTopRightRadius = style.borderTopRightRadius;
                shadow.style.borderBottomLeftRadius = style.borderBottomLeftRadius;
                shadow.style.borderBottomRightRadius = style.borderBottomRightRadius;
                shadow.style.rotate = style.rotate;
                // Тот же поворот вокруг той же точки своей плашки = панель, сдвинутая на offset.
                shadow.style.transformOrigin = new TransformOrigin(
                    new Length(style.transformOrigin.x, LengthUnit.Pixel),
                    new Length(style.transformOrigin.y, LengthUnit.Pixel));
                shadow.style.opacity = style.opacity;
            }

            target.RegisterCallback<AttachToPanelEvent>(_ => target.schedule.Execute(Place));
            target.RegisterCallback<GeometryChangedEvent>(_ => Place());
            target.RegisterCallback<DetachFromPanelEvent>(_ => shadow.RemoveFromHierarchy());
            // Показ/скрытие через display не всегда даёт GeometryChanged у самой панели — проверяем изредка.
            target.schedule.Execute(Sync).Every(250);
        }
    }
}
