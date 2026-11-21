using UnityEngine;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    public sealed class PodiumItemAnchor : MonoBehaviour
    {
        [SerializeField] private Transform holder;
        [SerializeField, Min(0)] private float surfaceClearance = 0.03f;

        private MeshRenderer holderRenderer;

        private void OnEnable()
        {
            holderRenderer = holder ? holder.GetComponent<MeshRenderer>() : null;
            AlignToHolder();
        }

        private void LateUpdate() => AlignToHolder();

        private void AlignToHolder()
        {
            if (!holderRenderer)
                return;

            Bounds bounds = holderRenderer.bounds;
            transform.position = new Vector3(bounds.center.x, bounds.max.y + surfaceClearance, bounds.center.z);
        }
    }
}
