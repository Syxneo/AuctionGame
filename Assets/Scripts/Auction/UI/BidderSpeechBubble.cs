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
        internal bool NeedsFrame => !background || !background.sprite;
        internal bool NeedsPointer => !pointer || !pointer.sprite;
        private float pointerWidth;
        private RectTransform dialogueRegion;
        private LayoutElement dialogueRow;
        private Vector2 lastDialogueSize;
        private const float DialoguePadding = 24;

        internal static BidderSpeechBubble CreateDialogue(BidderSpeechBubble prefab, RectTransform region,
            string labelName, int fontSize, LayoutElement row = null)
        {
            var bubble = prefab ? Instantiate(prefab, region) :
                PreparationUi.Fill("Speech Bubble", region).gameObject.AddComponent<BidderSpeechBubble>();
            bubble.Initialize(TMP_Settings.defaultFontAsset, null, null);
            if (bubble.NeedsFrame)
            {
                bubble.background.type = Image.Type.Simple;
                bubble.background.color = PreparationUi.Paper;
            }
            bubble.HidePointer();
            bubble.dialogueRegion = region;
            bubble.dialogueRow = row;
            bubble.Rect.anchorMin = bubble.Rect.anchorMax = bubble.Rect.pivot = new Vector2(.5f, 1);
            bubble.Rect.anchoredPosition = Vector2.zero;
            bubble.label.name = labelName;
            bubble.label.fontSize = fontSize;
            bubble.label.fontSizeMax = fontSize;
            bubble.label.fontSizeMin = 22;
            bubble.label.textWrappingMode = TextWrappingModes.Normal;
            bubble.label.overflowMode = TextOverflowModes.Overflow;
            bubble.label.alignment = TextAlignmentOptions.MidlineLeft;
            bubble.label.lineSpacing = 7;
            bubble.label.rectTransform.anchorMin = Vector2.zero;
            bubble.label.rectTransform.anchorMax = Vector2.one;
            bubble.label.rectTransform.offsetMin = Vector2.one * DialoguePadding;
            bubble.label.rectTransform.offsetMax = -Vector2.one * DialoguePadding;
            return bubble;
        }

        internal TMP_Text SetDialogue(string text)
        {
            label.text = text;
            gameObject.SetActive(true);
            FitDialogue();
            return label;
        }

        private void LateUpdate()
        {
            if (dialogueRegion && lastDialogueSize != dialogueRegion.rect.size)
                FitDialogue();
        }

        private void FitDialogue()
        {
            lastDialogueSize = dialogueRegion.rect.size;
            float width = Mathf.Max(1, lastDialogueSize.x);
            label.enableAutoSizing = false;
            label.fontSize = label.fontSizeMax;
            float height = label.GetPreferredValues(label.text, Mathf.Max(1, width - 2 * DialoguePadding),
                Mathf.Infinity).y + 2 * DialoguePadding;
            if (dialogueRow)
                dialogueRow.preferredHeight = height;
            else if (height > lastDialogueSize.y && lastDialogueSize.y > 0)
            {
                height = lastDialogueSize.y;
                label.enableAutoSizing = true;
            }
            Rect.sizeDelta = new Vector2(width, height);
        }

        internal void Initialize(TMP_FontAsset font, Sprite frame, Sprite arrow)
        {
            Rect.anchorMin = Rect.anchorMax = Rect.pivot = Vector2.one * 0.5f;
            Group = GetComponent<CanvasGroup>();
            Group.blocksRaycasts = false;
            Group.interactable = false;
            if (!background)
            {
                background = Child<Image>("Background");
                background.rectTransform.anchorMin = Vector2.zero;
                background.rectTransform.anchorMax = Vector2.one;
                background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
                background.type = Image.Type.Sliced;
            }
            if (!background.sprite)
                background.sprite = frame;
            background.raycastTarget = false;

            if (!label)
            {
                label = Child<TextMeshProUGUI>("Dialogue");
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = Vector2.one * 0.5f;
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.fontSize = 34;
                label.fontSizeMin = 28;
                label.fontSizeMax = 34;
                label.enableAutoSizing = true;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.color = new Color32(51, 44, 38, 255);
                label.richText = false;
                label.font = font;
            }
            else if (!label.font)
                label.font = font;
            label.raycastTarget = false;

            if (!pointer)
            {
                pointer = Child<Image>("Pointer");
                pointer.transform.SetAsFirstSibling();
                pointer.rectTransform.anchorMin = pointer.rectTransform.anchorMax = Vector2.one * 0.5f;
                pointer.rectTransform.pivot = new Vector2(0.5f, 0);
                pointer.rectTransform.sizeDelta = new Vector2(14, 0);
            }
            if (!pointer.sprite)
                pointer.sprite = arrow;
            pointer.raycastTarget = false;
            pointerWidth = pointer.rectTransform.sizeDelta.x;
            Hide();
        }

        internal void SetText(string text, float minWidth, float maxWidth, float padding, int maxLines)
        {
            label.text = text;
            float width = Mathf.Clamp(label.GetPreferredValues(text).x + 2 * padding, minWidth, maxWidth);
            padding = Mathf.Min(padding, (width - 1) / 2);
            float height = Mathf.Min(label.GetPreferredValues(text, width - 2 * padding, 0).y * 1.1f,
                maxLines * label.fontSizeMax * 1.2f) + 2 * padding;
            Rect.sizeDelta = new Vector2(width, Mathf.Max(60, height));
            label.rectTransform.sizeDelta = Rect.sizeDelta - Vector2.one * (2 * padding);
        }

        internal void SetSingleLine(string text, float minWidth, float maxWidth, float padding, float fontSize)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMin = 1f;
            label.fontSizeMax = fontSize;
            label.fontSize = fontSize;
            SetText(text, minWidth, maxWidth, padding, 1);
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
            pointer.rectTransform.sizeDelta = new Vector2(pointerWidth, direction.magnitude);
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg);
        }

        internal void SetBackgroundColor(Color color) => background.color = color;

        internal void HidePointer() => pointer.enabled = false;

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
