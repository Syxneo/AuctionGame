using UnityEngine;

namespace AuctionGame.Auction
{
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class AuctionItemSpritePresenter : MonoBehaviour
    {
        [SerializeField] private AuctionManager auction;
        [SerializeField] private Camera hallCamera;
        [SerializeField] private BidderDialoguePresenter dialogue;
        [SerializeField] private SceneIntroOutro stage;
        [SerializeField] private Vector2 maximumSize = new(3.8f, 4.8f);
        [Header("Item Reveal")]
        [SerializeField] private bool enableItemSpotlight = true;
        [SerializeField, Min(0.01f)] private float revealDuration = 0.4f;
        [SerializeField, Range(0.5f, 1f)] private float revealStartScale = 0.9f;
        [SerializeField] private Light podiumSpot;
        [SerializeField] private Color revealLightColor = new(1f, 0.79f, 0.28f);
        [SerializeField, Min(1f)] private float revealLightMultiplier = 2.5f;
        [SerializeField, Min(1f)] private float biddingLightMultiplier = 1.4f;

        [Header("Item Idle Shimmer")]
        [SerializeField] private bool enableItemShimmer = true;
        [SerializeField] private Vector2 shimmerInterval = new(3f, 6f);
        [SerializeField, Min(0.01f)] private float shimmerDuration = 0.65f;
        [SerializeField, Min(0.01f)] private float shimmerScale = 0.24f;
        [SerializeField] private Color shimmerColor = new Color32(227, 196, 119, 255);

        private SpriteRenderer sparkle;
        private Texture2D sparkleTexture;
        private Vector2 spriteMin;
        private Vector2 spriteMax;
        private double nextSparkle = double.PositiveInfinity;
        private double sparkleStarted;
        private int sparkleIndex;
        private SpriteRenderer visual;
        private Vector3 bottomCenter;
        private BidderSpeechBubble nameBubble;
        private Vector3 itemScale;
        private bool revealPending;
        private double revealStarted = double.NegativeInfinity;
        private float baseSpotIntensity;
        private Color baseSpotColor;
        private Color baseItemColor;
        private bool spotlightActive;

        internal Light PodiumSpot => podiumSpot;
        internal Color RevealLightColor => revealLightColor;

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
            ClearShimmer();
            ResetReveal();
            if (nameBubble)
                nameBubble.Hide();
        }

        private void OnDestroy()
        {
            if (sparkle)
                Destroy(sparkle.sprite);
            if (sparkleTexture)
                Destroy(sparkleTexture);
            if (nameBubble)
                Destroy(nameBubble.gameObject);
        }

        private void HandleStateChanged(AuctionState state)
        {
            if (state != AuctionState.Bidding)
                ClearShimmer();
            if (state is AuctionState.Idle or AuctionState.Finished)
                ShowItem(null);
            else if (state == AuctionState.Opening)
                ShowItem(auction.CurrentItem);
            else if (state is AuctionState.Sold or AuctionState.Unsold)
                ResetReveal();
        }

        private void ShowItem(AuctionItem item)
        {
            ClearShimmer();
            sparkleIndex = 0;
            ResetReveal();
            if (item && dialogue)
            {
                if (!nameBubble)
                {
                    nameBubble = dialogue.CreateBubble("Podium Item Name");
                    if (nameBubble)
                        nameBubble.transform.SetAsFirstSibling();
                }
                if (nameBubble)
                {
                    nameBubble.SetSingleLine(item.ItemName, 120, 270, 10, 27f);
                    nameBubble.SetBackgroundColor(new Color32(225, 200, 146, 255));
                }
            }
            else if (nameBubble)
                nameBubble.Hide();

            visual.forceRenderingOff = stage && !stage.OpeningItemVisible;
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

            spriteMin = min;
            spriteMax = max;
            Vector2 size = max - min;
            if (size.x <= 0f || size.y <= 0f)
            {
                visual.enabled = false;
                return;
            }

            float scale = Mathf.Min(maximumSize.x / size.x, maximumSize.y / size.y);
            itemScale = Vector3.one * scale;
            transform.localScale = itemScale;
            bottomCenter = new Vector3((min.x + max.x) * 0.5f, min.y);
            revealPending = enableItemSpotlight && auction && auction.State == AuctionState.ItemReveal;
            FaceCamera();
        }

        private void LateUpdate()
        {
            visual.forceRenderingOff = stage && !stage.OpeningItemVisible;
            if (visual.enabled)
            {
                UpdateReveal();
                FaceCamera();
            }
            UpdateSpotlight();
            UpdateShimmer(Time.unscaledTimeAsDouble);

            if (!nameBubble)
                return;

            bool showName = auction && auction.CurrentItem && (!stage || stage.IsStageRevealed) &&
                auction.State is AuctionState.ItemReveal or AuctionState.Bidding or AuctionState.Sold or AuctionState.Unsold;
            Vector3 anchor = transform.parent.position + Vector3.down * 1.05f;
            nameBubble.gameObject.SetActive(showName && dialogue.PlacePodiumLabel(nameBubble, anchor));
            if (nameBubble.gameObject.activeSelf)
                nameBubble.Group.alpha = 1f;
        }

        private void UpdateShimmer(double now)
        {
            if (!enableItemShimmer || !visual.enabled || visual.forceRenderingOff || !auction ||
                auction.State != AuctionState.Bidding || (stage && !stage.IsStageRevealed))
            {
                ClearShimmer();
                return;
            }

            if (double.IsPositiveInfinity(nextSparkle))
                ScheduleSparkle(now);
            if (now >= nextSparkle)
            {
                if (!sparkle)
                    CreateSparkle();

                float x = sparkleIndex % 2 == 0 ? 0.13f : 0.87f;
                float y = 0.65f + 0.12f * (sparkleIndex % 3);
                sparkle.transform.localPosition = new Vector3(
                    Mathf.Lerp(spriteMin.x, spriteMax.x, x),
                    Mathf.Lerp(spriteMin.y, spriteMax.y, y), -0.03f);
                sparkle.sortingLayerID = visual.sortingLayerID;
                sparkle.sortingOrder = visual.sortingOrder + 1;
                sparkleStarted = now;
                sparkle.enabled = true;
                sparkleIndex++;
                ScheduleSparkle(now);
            }

            if (!sparkle || !sparkle.enabled)
                return;

            float progress = (float)((now - sparkleStarted) / shimmerDuration);
            if (progress >= 1f)
            {
                sparkle.enabled = false;
                return;
            }

            float envelope = Mathf.Sin(Mathf.PI * progress);
            Color tint = shimmerColor;
            tint.a *= 0.8f * envelope;
            sparkle.color = tint;
            sparkle.transform.localScale = Vector3.one * (shimmerScale / itemScale.x) *
                Mathf.Lerp(0.45f, 1f, envelope);
        }

        private void ScheduleSparkle(double now)
        {
            // A repeating visual cycle keeps the cadence independent of gameplay RNG.
            float phase = (sparkleIndex * 0.618034f) % 1f;
            float interval = Mathf.Lerp(shimmerInterval.x, shimmerInterval.y, phase);
            nextSparkle = now + Mathf.Max(Mathf.Max(0.1f, shimmerDuration), interval);
        }

        private void ClearShimmer()
        {
            nextSparkle = double.PositiveInfinity;
            if (sparkle)
                sparkle.enabled = false;
        }

        private void CreateSparkle()
        {
            const int size = 24;
            sparkleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Podium Sparkle",
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                float edge = Mathf.Sqrt(u * 1.2f) + Mathf.Sqrt(v);
                byte alpha = (byte)(255f * Mathf.Clamp01((1.2f - edge) * 7f));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            sparkleTexture.SetPixels32(pixels);
            sparkleTexture.Apply(false, true);

            var child = new GameObject("Podium Idle Sparkle");
            child.transform.SetParent(transform, false);
            sparkle = child.AddComponent<SpriteRenderer>();
            sparkle.sprite = Sprite.Create(sparkleTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void ResetReveal()
        {
            if (spotlightActive)
            {
                if (podiumSpot)
                {
                    podiumSpot.intensity = baseSpotIntensity;
                    podiumSpot.color = baseSpotColor;
                }
                visual.color = baseItemColor;
            }
            spotlightActive = false;
            if (revealPending || !double.IsNegativeInfinity(revealStarted))
            {
                transform.localScale = itemScale;
                FaceCamera();
            }
            revealPending = false;
            revealStarted = double.NegativeInfinity;
        }

        private void UpdateReveal()
        {
            if (!enableItemSpotlight)
            {
                ResetReveal();
                return;
            }
            if (visual.forceRenderingOff || (stage && !stage.IsStageRevealed))
                return;
            double now = Time.unscaledTimeAsDouble;
            if (revealPending)
            {
                revealPending = false;
                revealStarted = now;
            }
            if (double.IsNegativeInfinity(revealStarted))
                return;

            float elapsed = (float)(now - revealStarted);
            float appear = Mathf.SmoothStep(0f, 1f, elapsed / revealDuration);
            transform.localScale = itemScale * Mathf.Lerp(revealStartScale, 1f, appear);
        }

        private void UpdateSpotlight()
        {
            if (!enableItemSpotlight || !visual.enabled || !auction)
                return;

            float? curtainProgress = stage ? stage.CurtainRevealProgress : null;
            bool biddingPresentation = auction.State is AuctionState.ItemReveal or AuctionState.Bidding;
            bool openingPresentation = auction.State == AuctionState.Opening && curtainProgress.HasValue;
            if (!biddingPresentation && !openingPresentation)
                return;
            if (!curtainProgress.HasValue && stage && !stage.IsStageRevealed)
                return;

            if (!spotlightActive)
            {
                if (podiumSpot)
                {
                    baseSpotIntensity = podiumSpot.intensity;
                    baseSpotColor = podiumSpot.color;
                }
                baseItemColor = visual.color;
                spotlightActive = true;
            }
            float progress = curtainProgress ?? 1f;
            float tintBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 1f, progress));
            // The item sprite can be unlit, so carry the spotlight tint in its renderer too.
            Color itemTint = Color.Lerp(Color.white, revealLightColor, 0.65f * (1f - tintBlend));
            itemTint.a = 1f;
            visual.color = baseItemColor * itemTint;

            if (!podiumSpot)
                return;
            float multiplier = Mathf.SmoothStep(revealLightMultiplier, biddingLightMultiplier, progress);
            float intensity = baseSpotIntensity * multiplier;
            if (!Mathf.Approximately(podiumSpot.intensity, intensity))
                podiumSpot.intensity = intensity;
            Color color = Color.Lerp(revealLightColor, baseSpotColor, tintBlend);
            if (!podiumSpot.color.Equals(color))
                podiumSpot.color = color;
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
