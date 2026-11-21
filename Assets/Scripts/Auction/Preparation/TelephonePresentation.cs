using UnityEngine;

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

        internal void Present(bool pickedUp, float rockDegrees, float rockPeriod)
        {
            artwork = (RectTransform)transform;
            answered = pickedUp;
            degrees = rockDegrees;
            period = rockPeriod;
            elapsed = 0;
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
        }
    }
}
