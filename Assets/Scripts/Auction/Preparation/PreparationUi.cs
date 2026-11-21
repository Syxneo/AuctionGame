using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    internal static class PreparationUi
    {
        internal static readonly Color Background = new(0.07f, 0.10f, 0.14f);
        internal static readonly Color Paper = new(0.98f, 0.95f, 0.88f);
        internal static readonly Color Ink = new(0.13f, 0.16f, 0.19f);
        internal static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max,
            Vector2 lower, Vector2 upper)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = lower;
            rect.offsetMax = upper;
            return rect;
        }

        internal static RectTransform Fill(string name, Transform parent) =>
            Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        internal static Image Surface(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        internal static TMP_Text Label(RectTransform rect, string value, int size, Color color,
            FontStyles style = FontStyles.Normal)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.richText = false;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        internal static Button Button(RectTransform rect, string caption, bool playClick = true)
        {
            var button = rect.gameObject.AddComponent<Button>();
            if (playClick)
                ButtonClickAudio.Bind(button);
            button.targetGraphic = Surface(rect, new Color(0.22f, 0.33f, 0.43f));
            button.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            Label(Fill("Label", rect), caption, 20, Color.white).alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
