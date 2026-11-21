using UnityEngine;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PodiumItemAnchor : MonoBehaviour
    {
        [SerializeField] private Camera hallCamera;
        [SerializeField] private Transform holder;
        [SerializeField] private Vector3 holderOffset;

        private RectTransform item;
        private RectTransform panel;

        private void Awake()
        {
            item = (RectTransform)transform;
            panel = item.parent as RectTransform;
        }

        private void LateUpdate()
        {
            if (!hallCamera || !holder || !panel)
                return;

            Vector3 screenPoint = hallCamera.WorldToScreenPoint(holder.TransformPoint(holderOffset));
            if (screenPoint.z > hallCamera.nearClipPlane &&
                RectTransformUtility.ScreenPointToWorldPointInRectangle(panel, screenPoint, null, out Vector3 point))
                item.position = point;
        }
    }
}
