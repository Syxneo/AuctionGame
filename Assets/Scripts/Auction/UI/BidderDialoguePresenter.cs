using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DefaultExecutionOrder(100)]
    public sealed class BidderDialoguePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class Binding
        {
            public string bidderId;
            public SpriteRenderer sprite;
            public BidderSpeechBubble Bubble { get; set; }
            public int Priority { get; set; }
            public bool Opening { get; set; }
            public double Started { get; set; }
            public double Ends { get; set; }
            public float AppearFor { get; set; }
            public float StartAlpha { get; set; }
            public float StartScale { get; set; }
            public string PendingText { get; set; }
            public bool CuePending { get; set; }
            public bool Replacing { get; set; }
            public Vector2 Anchor { get; set; }
            public Vector2 Destination { get; set; }
            public AIBidder Bidder { get; set; }
            public Material OriginalMaterial { get; set; }
            public Color OriginalColor { get; set; }
            public Vector3 RestPosition { get; set; }
            public Quaternion RestRotation { get; set; }
            public Vector3 RestScale { get; set; }
            public double ReactionStarted { get; set; } = double.NegativeInfinity;
            public bool Withdrawn { get; set; }
        }

        [SerializeField] private AuctionManager auction;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private BidderSpeechBubble bubblePrefab;
        [SerializeField] private AudioSource dialogueAudio;
        [SerializeField] private AudioClip dialogueCue;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Binding[] bidders = Array.Empty<Binding>();
        [SerializeField] private bool enableDialogueUi = true;
        [SerializeField] private bool showParticipation;
        [SerializeField] private Shader inactiveBidderShader;
        [SerializeField, Min(0)] private float swayDegrees = 2.5f;
        [SerializeField, Min(0)] private float hopHeight = 0.08f;
        [SerializeField, Min(1)] private float leaderScale = 1.07f;
        [SerializeField] private Color leaderTint = new(1f, 0.89f, 0.65f);
        [SerializeField, Min(20)] private float maxWidth = 460;
        [SerializeField, Min(20)] private float minWidth = 210;
        [SerializeField, Min(0)] private float verticalOffset = 24;
        [SerializeField, Min(0)] private float screenEdgePadding = 16;
        [SerializeField, Min(0)] private float textPadding = 18;
        [SerializeField, Min(1)] private int maxTextLines = 5;
        [SerializeField, Min(0)] private float horizontalPadding = 12;
        [SerializeField, Min(0)] private float verticalPadding = 8;
        [SerializeField, Min(0)] private float maxHorizontalShift = 100;
        [SerializeField, Min(0)] private float maxVerticalShift = 60;
        [SerializeField, Min(0.01f)] private float positionSmoothing = 0.12f;
        [SerializeField, Min(0.01f)] private float showDuration = 0.20f;
        [SerializeField, Min(0.01f)] private float hideDuration = 0.15f;
        [SerializeField, Min(0.01f)] private float replaceDuration = 0.15f;
        [SerializeField, Min(0.01f)] private float openingDuration = 3;
        [SerializeField, Min(0.01f)] private float ordinaryDuration = 2.5f;
        [SerializeField, Min(0.01f)] private float limitDuration = 3;
        [SerializeField, Min(0.01f)] private float withdrawalDuration = 3.5f;
        [SerializeField, Min(0.01f)] private float winnerDuration = 4;

        [Header("Bidder Reactions")]
        [SerializeField] private bool enableBidderFocus = true;
        [SerializeField] private bool enableWithdrawalReaction = true;
        [SerializeField, Min(0.01f)] private float reactionDuration = 0.3f;
        [SerializeField, Min(0f)] private float bidBounceDistance = 0.16f;
        [SerializeField, Min(0f)] private float withdrawalDistance = 0.12f;
        [SerializeField, Min(1f)] private float bidFocusScale = 1.06f;
        [SerializeField, Range(0.8f, 1f)] private float withdrawalScale = 0.95f;

        private readonly List<Binding> visible = new();
        private readonly List<Rect> occupied = new();
        private Sprite frame;
        private Sprite arrow;
        private Material inactiveMaterial;

        private void Awake()
        {
            if (!auction || !sceneCamera || !canvas)
            {
                enabled = false;
                return;
            }

            if (!bubblePrefab || bubblePrefab.NeedsFrame)
                frame = CreateSprite(true);
            if (!bubblePrefab || bubblePrefab.NeedsPointer)
                arrow = CreateSprite(false);
            if (showParticipation && inactiveBidderShader)
                inactiveMaterial = new Material(inactiveBidderShader);
            foreach (Binding binding in bidders)
            {
                if (showParticipation && binding.sprite)
                {
                    binding.OriginalMaterial = binding.sprite.sharedMaterial;
                    binding.OriginalColor = binding.sprite.color;
                    Transform character = binding.sprite.transform;
                    binding.RestPosition = character.localPosition;
                    binding.RestRotation = character.localRotation;
                    binding.RestScale = character.localScale;
                }
                binding.Bubble = CreateBubble(binding.bidderId + " Bubble");
            }
        }

        internal BidderSpeechBubble CreateBubble(string bubbleName)
        {
            if (!canvas)
                return null;

            var bubble = bubblePrefab ? Instantiate(bubblePrefab, canvas.transform, false)
                : new GameObject(bubbleName, typeof(RectTransform), typeof(BidderSpeechBubble))
                    .GetComponent<BidderSpeechBubble>();
            bubble.name = bubbleName;
            bubble.transform.SetParent(canvas.transform, false);
            bubble.Initialize(font ? font : TMP_Settings.defaultFontAsset, frame, arrow);
            return bubble;
        }

        internal bool PlaceSaleBubble(BidderSpeechBubble bubble, SpriteRenderer sprite,
            bool inFront = false, bool prioritize = false)
        {
            if (!canvas || !canvas.isActiveAndEnabled || canvas.targetDisplay != 0 || !sceneCamera)
                return false;

            var canvasRect = (RectTransform)canvas.transform;
            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!TryProject(sprite, canvasRect, canvasCamera, out Vector2 anchor, inFront ? .45f : 1f))
                return false;

            Rect safe = SafeRect(canvasRect, canvasCamera);
            Vector2 size = bubble.Rect.sizeDelta;
            if (inFront)
            {
                bubble.Rect.anchoredPosition = ClampCenter(anchor + new Vector2(56f, 42f), size, safe);
                bubble.HidePointer();
                return true;
            }
            Vector2 preferred = anchor + Vector2.up * (verticalOffset + size.y / 2);
            for (float lift = 0; lift <= maxVerticalShift; lift += 8f)
            {
                Vector2 center = ClampCenter(preferred + Vector2.up * lift, size, safe);
                var body = new Rect(center - size / 2, size);
                if (OverlapsBidderBubble(body))
                    continue;

                bubble.Rect.anchoredPosition = center;
                bubble.DrawPointer(anchor);
                return true;
            }

            if (!prioritize)
                return false;

            bubble.Rect.anchoredPosition = ClampCenter(preferred, size, safe);
            bubble.DrawPointer(anchor);
            return true;
        }

        internal bool PlacePodiumLabel(BidderSpeechBubble bubble, Vector3 worldAnchor)
        {
            if (!canvas || !canvas.isActiveAndEnabled || canvas.targetDisplay != 0 || !sceneCamera)
                return false;

            var canvasRect = (RectTransform)canvas.transform;
            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!TryProject(worldAnchor, canvasRect, canvasCamera, out Vector2 anchor))
                return false;

            bubble.Rect.anchoredPosition = ClampCenter(anchor, bubble.Rect.sizeDelta, SafeRect(canvasRect, canvasCamera));
            bubble.HidePointer();
            return true;
        }

        private bool OverlapsBidderBubble(Rect body)
        {
            foreach (Binding binding in visible)
            {
                var bubble = binding.Bubble;
                Vector2 size = bubble.Rect.sizeDelta;
                if (bubble.gameObject.activeSelf && body.Overlaps(new Rect(bubble.Rect.anchoredPosition - size / 2, size)))
                    return true;
            }
            return false;
        }

        internal void PlaySaleCue()
        {
            if (!HasActiveDialogue && dialogueAudio && dialogueCue && !dialogueAudio.isPlaying)
                dialogueAudio.PlayOneShot(dialogueCue);
        }

        private void OnEnable()
        {
            if (!auction || !sceneCamera || !canvas)
                return;
            auction.OnAiDialogue += Show;
            auction.OnItemChanged += ClearForLot;
            auction.OnAuctionEnded += EndLot;
            auction.OnAuctionPhaseFinished += Clear;
            if (showParticipation)
            {
                auction.OnStateChanged += ClearParticipation;
                auction.OnBidPlaced += ReactToBid;
                auction.OnBidderChickenedOut += ReactToWithdrawal;
                if (auction.CurrentItem)
                    UpdateParticipation();
            }
        }

        private void OnDisable()
        {
            if (!auction)
                return;
            auction.OnAiDialogue -= Show;
            auction.OnItemChanged -= ClearForLot;
            auction.OnAuctionEnded -= EndLot;
            auction.OnAuctionPhaseFinished -= Clear;
            auction.OnStateChanged -= ClearParticipation;
            auction.OnBidPlaced -= ReactToBid;
            auction.OnBidderChickenedOut -= ReactToWithdrawal;
            ClearParticipation(AuctionState.Idle);
            Clear();
        }

        private void OnDestroy()
        {
            foreach (Binding binding in bidders)
            {
                if (binding.Bubble)
                    Destroy(binding.Bubble.gameObject);
            }
            Release(frame);
            Release(arrow);
            if (inactiveMaterial)
                Destroy(inactiveMaterial);
        }

        private static void Release(Sprite sprite)
        {
            if (!sprite)
                return;
            Destroy(sprite.texture);
            Destroy(sprite);
        }

        private void ClearForLot(AuctionItem _)
        {
            Clear();
            if (showParticipation)
                UpdateParticipation();
        }

        private void UpdateParticipation()
        {
            foreach (Binding binding in bidders)
            {
                binding.Bidder = null;
                binding.ReactionStarted = double.NegativeInfinity;
                binding.Withdrawn = false;
                foreach (AIBidder bidder in auction.Opponents)
                {
                    if (binding.bidderId == bidder.Id)
                    {
                        binding.Bidder = bidder;
                        binding.Withdrawn = bidder.HasChickenedOut;
                        break;
                    }
                }
            }
            UpdateCharacters(Time.timeAsDouble);
        }

        private void ClearParticipation(AuctionState state)
        {
            if (!showParticipation || (state != AuctionState.Idle && state != AuctionState.Finished))
                return;

            foreach (Binding binding in bidders)
            {
                binding.Bidder = null;
                binding.Withdrawn = false;
                binding.ReactionStarted = double.NegativeInfinity;
            }
            UpdateCharacters(Time.timeAsDouble);
        }

        private void ReactToBid(BidRecord bid)
        {
            if (!enableBidderFocus || bid.bidderId == BidderIds.Player)
                return;

            foreach (Binding binding in bidders)
                if (binding.Bidder?.Id == bid.bidderId && !binding.Withdrawn)
                    binding.ReactionStarted = Time.unscaledTimeAsDouble;
        }

        private void ReactToWithdrawal(AIBidder bidder)
        {
            foreach (Binding binding in bidders)
            {
                if (binding.Bidder != bidder || binding.Withdrawn)
                    continue;

                binding.Withdrawn = true;
                binding.ReactionStarted = enableWithdrawalReaction
                    ? Time.unscaledTimeAsDouble : double.NegativeInfinity;
            }
        }

        private void UpdateCharacters(double now)
        {
            bool animate = auction.State is AuctionState.ItemReveal or AuctionState.Bidding;
            for (int index = 0; index < bidders.Length; index++)
            {
                Binding binding = bidders[index];
                if (!binding.sprite)
                    continue;

                bool inactive = binding.Bidder != null && (!binding.Bidder.IsActiveForLot || binding.Withdrawn);
                bool moving = animate && binding.Bidder != null && !inactive;
                bool leading = !inactive && binding.Bidder != null && binding.bidderId == auction.HighestBidderId;
                Material material = inactive && inactiveMaterial ? inactiveMaterial : binding.OriginalMaterial;
                if (binding.sprite.sharedMaterial != material)
                    binding.sprite.sharedMaterial = material;
                Color color = leading ? binding.OriginalColor * leaderTint : binding.OriginalColor;
                color.a = binding.OriginalColor.a;
                binding.sprite.color = color;

                float phase = (float)(now * 2.2 + index * 1.35);
                float sway = moving ? swayDegrees * Mathf.Sin(phase) : 0f;
                float hop = moving ? hopHeight * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(phase * 1.5f)), 2f) : 0f;
                float scale = leading ? leaderScale : 1f;
                float progress = (float)((Time.unscaledTimeAsDouble - binding.ReactionStarted) / reactionDuration);
                bool reactionEnabled = binding.Withdrawn ? enableWithdrawalReaction : enableBidderFocus;
                if (auction.State == AuctionState.Bidding && reactionEnabled && progress < 1f)
                {
                    float pulse = Mathf.Sin(Mathf.PI * progress);
                    hop = Mathf.Lerp(hop, binding.Withdrawn ? -withdrawalDistance : bidBounceDistance, pulse);
                    scale = Mathf.Lerp(scale, binding.Withdrawn ? withdrawalScale : scale * bidFocusScale, pulse);
                }
                else
                    binding.ReactionStarted = double.NegativeInfinity;

                Transform character = binding.sprite.transform;
                character.localPosition = binding.RestPosition + Vector3.up * hop;
                character.localRotation = binding.RestRotation * Quaternion.Euler(0f, 0f, sway);
                character.localScale = binding.RestScale * scale;
            }
        }

        private void Clear()
        {
            foreach (Binding binding in bidders)
                Hide(binding);
        }

        private void EndLot(AuctionResult result)
        {
            if (auction.PacesAiInteractions)
                return;

            foreach (Binding binding in bidders)
            {
                if (binding.Priority != 5 || binding.bidderId != result.winnerId)
                    Hide(binding);
            }
        }

        internal bool HasActiveDialogue
        {
            get
            {
                if (!isActiveAndEnabled || !enableDialogueUi)
                    return false;
                double now = Time.unscaledTimeAsDouble;
                foreach (Binding binding in bidders)
                    if (binding.Ends > now)
                        return true;
                return false;
            }
        }

        private static void Hide(Binding binding)
        {
            binding.Ends = 0;
            binding.PendingText = null;
            binding.CuePending = false;
            if (binding.Bubble)
                binding.Bubble.Hide();
        }

        private void Show(string bidderId, string category, string line)
        {
            if (!enableDialogueUi || category.StartsWith("SALE-", StringComparison.Ordinal))
                return;

            Binding target = Array.Find(bidders, binding => binding.bidderId == bidderId);
            if (target == null || !target.sprite)
                return;

            bool opening = category == "OPENING";
            int priority = category switch
            {
                "OPENING" => 0,
                "BR-01" or "BR-02" => 1,
                "BR-03" => 2,
                _ when category.StartsWith("BR-", StringComparison.Ordinal) => 3,
                _ when category.StartsWith("WD-", StringComparison.Ordinal) => 4,
                _ when category.StartsWith("WIN-", StringComparison.Ordinal) => 5,
                _ => -1
            };
            if (priority < 0)
                return;

            if (auction.PacesAiInteractions)
                Clear();

            double now = Time.unscaledTimeAsDouble;
            bool replacing = target.Ends > now;
            if (replacing && target.Priority > priority)
                return;

            if (priority is >= 1 and <= 3)
            {
                int count = 0;
                Binding lowest = null;
                foreach (Binding binding in bidders)
                {
                    if (binding == target || binding.Ends <= now || binding.Opening || binding.Priority > 3)
                        continue;
                    count++;
                    if (lowest == null || binding.Priority < lowest.Priority)
                        lowest = binding;
                }
                if (count >= 2)
                {
                    if (lowest.Priority > priority)
                        return;
                    Hide(lowest);
                }
            }

            float duration = priority switch
            {
                0 => openingDuration,
                1 => ordinaryDuration,
                4 => withdrawalDuration,
                5 => winnerDuration,
                _ => limitDuration
            };
            if (auction.PacesAiInteractions)
                duration = Mathf.Min(duration, auction.AiInteractionInterval);
            target.Priority = priority;
            target.Opening = opening;
            target.Started = now;
            target.Ends = now + duration;
            target.AppearFor = replacing ? replaceDuration : showDuration;
            target.StartAlpha = replacing ? target.Bubble.Group.alpha : 0;
            target.StartScale = replacing ? target.Bubble.Rect.localScale.x : 0.9f;
            target.PendingText = replacing && target.Bubble.gameObject.activeSelf ? line : null;
            target.Replacing = target.PendingText != null;
            if (target.PendingText == null)
                SetText(target, line);
        }

        private void SetText(Binding binding, string line)
        {
            var canvasRect = (RectTransform)canvas.transform;
            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float availableWidth = Mathf.Max(1f, SafeRect(canvasRect, canvasCamera).width);
            float width = Mathf.Min(maxWidth, availableWidth);
            binding.Bubble.SetText(line, Mathf.Min(minWidth, width), width, textPadding, maxTextLines);
            binding.CuePending = true;
        }

        private void LateUpdate()
        {
            if (showParticipation)
                UpdateCharacters(Time.timeAsDouble);
            Render(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
        }

        private void Render(double now, float delta)
        {
            if (!enableDialogueUi)
            {
                Clear();
                return;
            }

            var canvasRect = (RectTransform)canvas.transform;
            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect safe = SafeRect(canvasRect, canvasCamera);
            visible.Clear();
            foreach (Binding binding in bidders)
            {
                if (binding.Ends <= now)
                {
                    Hide(binding);
                    continue;
                }

                if (!TryProject(binding.sprite, canvasRect, canvasCamera, out Vector2 anchor))
                {
                    binding.Bubble.Hide();
                    continue;
                }

                binding.Anchor = anchor;
                if (binding.PendingText != null && now - binding.Started >= binding.AppearFor / 2)
                {
                    SetText(binding, binding.PendingText);
                    binding.PendingText = null;
                    binding.StartAlpha = 0;
                }
                int index = 0;
                while (index < visible.Count && visible[index].Priority >= binding.Priority)
                    index++;
                visible.Insert(index, binding);
            }

            Layout(safe);
            bool cueRequested = false;
            foreach (Binding binding in visible)
            {
                var bubble = binding.Bubble;
                bubble.gameObject.SetActive(true);
                bubble.Rect.anchoredPosition = bubble.Positioned
                    ? Vector2.SmoothDamp(bubble.Rect.anchoredPosition, binding.Destination, ref bubble.Velocity,
                        positionSmoothing, Mathf.Infinity, delta)
                    : binding.Destination;
                bubble.Positioned = true;
                bubble.DrawPointer(binding.Anchor);
                float show = Mathf.Clamp01((float)(now - binding.Started) / binding.AppearFor);
                float fade = Mathf.Clamp01((float)(binding.Ends - now) / hideDuration);
                float fadeIn = binding.Replacing ? Mathf.Clamp01(2 * show - 1) : show;
                float easeOut = fadeIn * (2 - fadeIn);
                float alpha = binding.PendingText != null ? binding.StartAlpha * (1 - 2 * show)
                    : Mathf.Lerp(binding.StartAlpha, 1, easeOut);
                bubble.Group.alpha = alpha * (fade * (2 - fade));
                bubble.Rect.localScale = Vector3.one * Mathf.Lerp(binding.StartScale, 1, show * (2 - show));
                if (binding.CuePending && bubble.Group.alpha > 0f)
                {
                    binding.CuePending = false;
                    cueRequested = true;
                }
            }

            if (cueRequested && dialogueAudio && dialogueCue)
            {
                dialogueAudio.Stop();
                dialogueAudio.PlayOneShot(dialogueCue);
            }
        }

        private Rect SafeRect(RectTransform canvasRect, Camera canvasCamera)
        {
            Rect screen = Screen.safeArea;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.min, canvasCamera, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.max, canvasCamera, out Vector2 max);
            min -= canvasRect.rect.center;
            max -= canvasRect.rect.center;
            return Rect.MinMaxRect(min.x + screenEdgePadding, min.y + screenEdgePadding,
                max.x - screenEdgePadding, max.y - screenEdgePadding);
        }

        private bool TryProject(SpriteRenderer sprite, RectTransform canvasRect, Camera canvasCamera,
            out Vector2 anchor, float anchorHeight = 1f)
        {
            anchor = default;
            if (!sprite || !sprite.enabled || !sprite.gameObject.activeInHierarchy)
                return false;

            Bounds bounds = sprite.bounds;
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, anchorHeight);
            return TryProject(new Vector3(bounds.center.x, y, bounds.center.z), canvasRect, canvasCamera, out anchor);
        }

        private bool TryProject(Vector3 worldAnchor, RectTransform canvasRect, Camera canvasCamera, out Vector2 anchor)
        {
            anchor = default;
            Vector3 screen = sceneCamera.WorldToScreenPoint(worldAnchor);
            if (!(screen.z >= sceneCamera.nearClipPlane && screen.z <= sceneCamera.farClipPlane &&
                sceneCamera.pixelRect.Contains((Vector2)screen) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvasCamera, out anchor)))
                return false;
            anchor -= canvasRect.rect.center;
            return true;
        }

        private void Layout(Rect safe)
        {
            occupied.Clear();
            foreach (Binding binding in visible)
            {
                Vector2 size = binding.Bubble.Rect.sizeDelta;
                Vector2 preferred = binding.Anchor + Vector2.up * (verticalOffset + size.y / 2);
                if (!TryPlace(preferred, size, safe, out Vector2 center))
                {
                    // Keep a deterministic stack when the local displacement budget is insufficient.
                    float top = safe.yMax;
                    foreach (Binding fallback in visible)
                    {
                        Vector2 dimensions = fallback.Bubble.Rect.sizeDelta;
                        fallback.Destination = ClampCenter(new Vector2(fallback.Anchor.x, top - dimensions.y / 2), dimensions, safe);
                        top -= dimensions.y + verticalPadding;
                    }
                    return;
                }
                binding.Destination = center;
                occupied.Add(new Rect(center - size / 2 - new Vector2(horizontalPadding, verticalPadding) / 2,
                    size + new Vector2(horizontalPadding, verticalPadding)));
            }
        }

        private bool TryPlace(Vector2 preferred, Vector2 size, Rect safe, out Vector2 center)
        {
            const float step = 4;
            for (float vertical = 0; vertical <= maxVerticalShift; vertical += step)
            {
                for (float horizontal = 0; horizontal <= maxHorizontalShift; horizontal += step)
                {
                    for (int side = 0; side < 4; side++)
                    {
                        center = ClampCenter(preferred + new Vector2(side % 2 == 0 ? horizontal : -horizontal,
                            side < 2 ? vertical : -vertical), size, safe);
                        var bounds = new Rect(center - size / 2, size);
                        bool overlaps = false;
                        foreach (Rect other in occupied)
                            overlaps |= other.Overlaps(bounds);
                        if (!overlaps)
                            return true;
                    }
                }
            }
            center = default;
            return false;
        }

        private static Vector2 ClampCenter(Vector2 center, Vector2 size, Rect safe)
        {
            Vector2 half = size * 0.5f;
            return new Vector2(Mathf.Clamp(center.x, safe.xMin + half.x, safe.xMax - half.x),
                Mathf.Clamp(center.y, safe.yMin + half.y, safe.yMax - half.y));
        }

        private static Sprite CreateSprite(bool rounded)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var fill = new Color32(247, 240, 223, 255);
            var border = new Color32(185, 168, 137, 255);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float side = Mathf.Abs(x + 0.5f - size / 2);
                    if (rounded)
                    {
                        Vector2 corner = new(Mathf.Max(0, side - 7), Mathf.Max(0, Mathf.Abs(y + 0.5f - size / 2) - 7));
                        if (corner.magnitude < 9)
                            pixels[y * size + x] = corner.magnitude > 7.5f ? border : fill;
                    }
                    else
                    {
                        float width = (y + 1) / 2f;
                        if (side < width)
                            pixels[y * size + x] = width - side < 2 ? border : fill;
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one / 2, 100, 0,
                SpriteMeshType.FullRect, rounded ? Vector4.one * 10 : Vector4.zero);
        }
    }
}
