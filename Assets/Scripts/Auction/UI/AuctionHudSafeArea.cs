using UnityEngine;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AuctionHudSafeArea : MonoBehaviour
    {
        private RectTransform content;
        private Rect appliedArea;
        private Vector2Int appliedScreenSize;

        private void OnEnable()
        {
            content = (RectTransform)transform;
            appliedScreenSize = Vector2Int.zero;
            UpdateSafeArea();
        }

        private void LateUpdate() => UpdateSafeArea();

        private void UpdateSafeArea()
        {
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            Rect safeArea = Screen.safeArea;
            if (screenSize.x <= 0 || screenSize.y <= 0 || safeArea.width <= 0 || safeArea.height <= 0 ||
                (screenSize == appliedScreenSize && safeArea == appliedArea))
                return;

            Apply(safeArea, screenSize);
            appliedArea = safeArea;
            appliedScreenSize = screenSize;
        }

        private void Apply(Rect safeArea, Vector2 screenSize)
        {
            content.anchorMin = new Vector2(Mathf.Clamp01(safeArea.xMin / screenSize.x),
                Mathf.Clamp01(safeArea.yMin / screenSize.y));
            content.anchorMax = new Vector2(Mathf.Clamp01(safeArea.xMax / screenSize.x),
                Mathf.Clamp01(safeArea.yMax / screenSize.y));
            content.offsetMin = content.offsetMax = Vector2.zero;
        }
    }
}
