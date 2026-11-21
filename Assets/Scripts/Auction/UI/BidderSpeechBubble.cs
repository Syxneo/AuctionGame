using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class BidderSpeechBubble : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image pointer;

        internal RectTransform Rect => (RectTransform)transform;
        internal CanvasGroup Group { get; private set; }
        internal Vector2 Velocity;
        internal bool Positioned;

        internal void Initialize(TMP_FontAsset font, Sprite frame, Sprite arrow)
        {
            Rect.anchorMin = Rect.anchorMax = Rect.pivot = Vector2.one * 0.5f;
            Group = GetComponent<CanvasGroup>();
            Group.blocksRaycasts = false;
            Group.interactable = false;
            if (!background)
                background = Child<Image>("Background");
            if (!label)
                label = Child<TextMeshProUGUI>("Dialogue");
            if (!pointer)
                pointer = Child<Image>("Pointer");

            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
            background.sprite = frame;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            label.font = font;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = Vector2.one * 0.5f;
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.fontSize = 22;
            label.fontSizeMin = 12;
            label.fontSizeMax = 22;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = new Color32(51, 44, 38, 255);
            label.richText = false;
            label.raycastTarget = false;
            pointer.sprite = arrow;
            pointer.raycastTarget = false;
            pointer.transform.SetAsFirstSibling();
            pointer.rectTransform.anchorMin = pointer.rectTransform.anchorMax = Vector2.one * 0.5f;
            pointer.rectTransform.pivot = new Vector2(0.5f, 0);
            Hide();
        }

        internal void SetText(string text, float minWidth, float maxWidth, float padding, int maxLines)
        {
            label.text = text;
            float width = Mathf.Clamp(label.GetPreferredValues(text).x + 2 * padding, minWidth, maxWidth);
            padding = Mathf.Min(padding, (width - 1) / 2);
            float height = Mathf.Min(label.GetPreferredValues(text, width - 2 * padding, 0).y * 1.1f,
                maxLines * 26) + 2 * padding;
            Rect.sizeDelta = new Vector2(width, Mathf.Max(44, height));
            label.rectTransform.sizeDelta = Rect.sizeDelta - Vector2.one * (2 * padding);
        }

        internal void DrawPointer(Vector2 anchor)
        {
            Vector2 toward = anchor - Rect.anchoredPosition;
            Vector2 half = Rect.sizeDelta * 0.5f;
            float edge = Mathf.Max(Mathf.Abs(toward.x) / half.x, Mathf.Abs(toward.y) / half.y);
            Vector2 start = edge > 1 ? toward / edge * 0.94f : toward;
            Vector2 direction = toward - start;
            pointer.enabled = direction.sqrMagnitude > 4;
            pointer.rectTransform.anchoredPosition = start;
            pointer.rectTransform.sizeDelta = new Vector2(14, direction.magnitude);
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg);
        }

        internal void Hide()
        {
            Positioned = false;
            Velocity = Vector2.zero;
            gameObject.SetActive(false);
        }

        private T Child<T>(string name) where T : Graphic
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(transform, false);
            return child.AddComponent<T>();
        }
    }
}
