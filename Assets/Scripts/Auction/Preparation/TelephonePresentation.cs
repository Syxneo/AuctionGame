using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(RectTransform))]
    internal sealed class TelephonePresentation : MonoBehaviour
    {
        private RectTransform artwork;
        private bool answered;
        private float degrees;
        private float period;
        private float elapsed;
        private CanvasGroup ringLines;
        private RectTransform leftLine;
        private RectTransform rightLine;
        private float ringOpacity;
        private float ringScale;
        private float artworkAspect;

        internal void Present(bool pickedUp, float rockDegrees, float rockPeriod, float lineOpacity, float lineScale)
        {
            artwork = (RectTransform)transform;
            answered = pickedUp;
            degrees = rockDegrees;
            period = rockPeriod;
            elapsed = 0;
            if (!answered && lineOpacity > 0)
            {
                ringOpacity = lineOpacity;
                ringScale = lineScale;
                var image = GetComponent<Image>();
                artworkAspect = image && image.sprite ? image.sprite.rect.width / image.sprite.rect.height : 1f;
                var layer = PreparationUi.Fill("Telephone Ring Lines", artwork);
                ringLines = layer.gameObject.AddComponent<CanvasGroup>();
                ringLines.alpha = 0;
                ringLines.blocksRaycasts = false;
                leftLine = BuildRingLine(layer, -1);
                rightLine = BuildRingLine(layer, 1);
            }
        }

        private static RectTransform BuildRingLine(RectTransform layer, float side)
        {
            var center = Vector2.one * .5f;
            var line = PreparationUi.Rect("Ink Vibration Line", layer, center, center, Vector2.zero, Vector2.zero);
            for (int i = 0; i < 3; i++)
            {
                var segment = PreparationUi.Rect("Ink Stroke", line, center, center, Vector2.zero, Vector2.zero);
                segment.anchoredPosition = new Vector2(side * (5 - 3 * i - i * i), 8 * (i - 1));
                segment.sizeDelta = new Vector2(13 - i, 3 - .45f * i);
                segment.localRotation = Quaternion.Euler(0, 0, side * (105 + i * 17));
                PreparationUi.Surface(segment, new Color(.24f, .15f, .09f)).raycastTarget = false;
            }
            return line;
        }

        private void Update()
        {
            if (!artwork)
                return;

            elapsed += Time.unscaledDeltaTime;
            if (answered)
            {
                float lift = Mathf.SmoothStep(0, 1, elapsed / .22f);
                artwork.anchoredPosition = Vector2.up * (22 * lift);
                artwork.localRotation = Quaternion.Euler(0, 0, -8 * lift);
                enabled = elapsed < .22f;
                return;
            }

            float swing = Mathf.Sin(elapsed / Mathf.Max(.1f, period) * 2 * Mathf.PI);
            artwork.localRotation = Quaternion.Euler(0, 0, degrees * swing);
            if (ringLines)
            {
                float pulse = swing * swing;
                float width = Mathf.Min(artwork.rect.width, artwork.rect.height * artworkAspect);
                float height = width / artworkAspect;
                leftLine.anchoredPosition = new Vector2(-width * .49f, height * .32f);
                rightLine.anchoredPosition = new Vector2(width * .49f, height * .32f);
                leftLine.localScale = rightLine.localScale = Vector3.one * (ringScale * (.82f + .18f * pulse));
                ringLines.alpha = ringOpacity * pulse;
            }
        }

        private void OnDisable()
        {
            if (ringLines)
                ringLines.alpha = 0;
        }
    }
}
