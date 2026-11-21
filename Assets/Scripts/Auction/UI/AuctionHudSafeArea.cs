using UnityEngine;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AuctionHudSafeArea : MonoBehaviour
    {
        [SerializeField] private RectTransform dockBackground;

        private RectTransform content;
        private Rect appliedArea;
        private Vector2Int appliedScreenSize;
        private Vector2 appliedParentSize;

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
            Vector2 parentSize = content.parent is RectTransform parent ? parent.rect.size : Vector2.zero;
            if (screenSize.x <= 0 || screenSize.y <= 0 || safeArea.width <= 0 || safeArea.height <= 0 ||
                (screenSize == appliedScreenSize && safeArea == appliedArea && parentSize == appliedParentSize))
                return;

            Apply(safeArea, screenSize, parentSize);
            appliedArea = safeArea;
            appliedScreenSize = screenSize;
            appliedParentSize = parentSize;
        }

        private void Apply(Rect safeArea, Vector2 screenSize, Vector2 parentSize)
        {
            content.anchorMin = new Vector2(Mathf.Clamp01(safeArea.xMin / screenSize.x),
                Mathf.Clamp01(safeArea.yMin / screenSize.y));
            content.anchorMax = new Vector2(Mathf.Clamp01(safeArea.xMax / screenSize.x),
                Mathf.Clamp01(safeArea.yMax / screenSize.y));
            content.offsetMin = content.offsetMax = Vector2.zero;

            if (dockBackground)
            {
                // The dock stays inside the safe area while its background covers the screen edges.
                dockBackground.offsetMin = -Vector2.Scale(content.anchorMin, parentSize);
                dockBackground.offsetMax = new Vector2((1f - content.anchorMax.x) * parentSize.x, 0f);
            }
        }
    }
}
