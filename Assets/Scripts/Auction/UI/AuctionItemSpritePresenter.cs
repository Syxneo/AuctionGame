using UnityEngine;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class AuctionItemSpritePresenter : MonoBehaviour
    {
        [SerializeField] private AuctionManager auction;
        [SerializeField] private Camera hallCamera;
        [SerializeField] private BidderDialoguePresenter dialogue;
        [SerializeField] private SceneIntroOutro stage;
        [SerializeField] private Vector2 maximumSize = new(3.8f, 4.8f);

        private SpriteRenderer visual;
        private Vector3 bottomCenter;
        private BidderSpeechBubble nameBubble;

        private void Awake() => visual = GetComponent<SpriteRenderer>();

        private void OnEnable()
        {
            if (auction)
            {
                auction.OnItemChanged += ShowItem;
                auction.OnStateChanged += HandleStateChanged;
            }

            ShowItem(auction ? auction.CurrentItem : null);
        }

        private void OnDisable()
        {
            if (auction)
            {
                auction.OnItemChanged -= ShowItem;
                auction.OnStateChanged -= HandleStateChanged;
            }

            visual.enabled = false;
            if (nameBubble)
                nameBubble.Hide();
        }

        private void OnDestroy()
        {
            if (nameBubble)
                Destroy(nameBubble.gameObject);
        }

        private void HandleStateChanged(AuctionState state)
        {
            if (state is AuctionState.Idle or AuctionState.Finished)
                ShowItem(null);
        }

        private void ShowItem(AuctionItem item)
        {
            if (item && dialogue)
            {
                if (!nameBubble)
                    nameBubble = dialogue.CreateBubble("Podium Item Name");
                if (nameBubble)
                {
                    nameBubble.SetSingleLine(item.ItemName, 120, 270, 10, 27f);
                    nameBubble.SetBackgroundColor(new Color32(225, 200, 146, 255));
                }
            }
            else if (nameBubble)
                nameBubble.Hide();

            visual.sprite = item ? item.Sprite : null;
            visual.enabled = visual.sprite != null;
            if (!visual.enabled)
                return;

            // A Tight sprite mesh excludes the transparent margins of the artwork.
            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;
            foreach (Vector2 vertex in visual.sprite.vertices)
            {
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }

            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f)
            {
                visual.enabled = false;
                return;
            }

            float scale = Mathf.Min(maximumSize.x / size.x, maximumSize.y / size.y);
            transform.localScale = Vector3.one * scale;
            bottomCenter = new Vector3((min.x + max.x) * 0.5f, min.y);
            FaceCamera();
        }

        private void LateUpdate()
        {
            if (visual.enabled)
                FaceCamera();

            if (!nameBubble)
                return;

            bool showName = auction && auction.CurrentItem && (!stage || stage.IsStageRevealed) &&
                auction.State is AuctionState.ItemReveal or AuctionState.Bidding or AuctionState.Sold or AuctionState.Unsold;
            Vector3 anchor = transform.parent.position + Vector3.down * 1.05f;
            nameBubble.gameObject.SetActive(showName && dialogue.PlacePodiumLabel(nameBubble, anchor));
            if (nameBubble.gameObject.activeSelf)
                nameBubble.Group.alpha = 1f;
        }

        private void FaceCamera()
        {
            Vector3 anchor = transform.parent.position;
            Quaternion rotation = transform.rotation;
            if (hallCamera)
            {
                // Sprites face local -Z. Keep the bottom edge level with the podium.
                Vector3 forward = hallCamera.orthographic ? hallCamera.transform.forward :
                    anchor - hallCamera.transform.position;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    rotation = Quaternion.LookRotation(forward);
            }

            transform.rotation = rotation;
            transform.position = anchor - transform.TransformVector(bottomCenter);
        }
    }
}
