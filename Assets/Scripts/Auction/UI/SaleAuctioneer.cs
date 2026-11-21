using UnityEngine;

namespace AuctionGame.Auction
{
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SaleAuctioneer : MonoBehaviour
    {
        internal enum Pose { Idle, Speaking, Excited, Disappointed }

        [SerializeField] private AuctionManager auction;
        [SerializeField] private BidderDialoguePresenter dialogue;
        [SerializeField, Min(0)] private float bidCooldown = 3f;
        [SerializeField] private bool playDialogueCue;
        [Header("Bid Impact")]
        [SerializeField] private bool enableBidImpact = true;
        [SerializeField, Min(0.01f)] private float bidImpactDuration = 0.3f;
        [SerializeField, Min(1)] private float pricePulseScale = 1.2f;
        [SerializeField, Min(1)] private float bubblePulseScale = 1.08f;
        [SerializeField] private Color bidFlashColor = new(0.21f, 0.53f, 0.26f);
        [Header("Gavel Slam")]
        [SerializeField] private bool enableGavelSlam = true;
        [SerializeField, Min(0.01f)] private float gavelDuration = 0.28f;
        [SerializeField] private AudioClip gavelImpactClip;

        [Header("Countdown Tension")]
        [SerializeField] private bool enableCountdownTension = true;
        [SerializeField, Range(0f, AuctionManager.AntiSnipingTime)] private float warningThreshold = 3f;
        [SerializeField, Min(0.01f)] private float warningPulseDuration = 1f;
        [SerializeField, Min(1f)] private float warningPulseScale = 1.10f;

        private Vector3 dialogueRestScale;
        private bool warningActive;
        private SpriteRenderer sprite;
        private BidderSpeechBubble bubble;
        private BidderSpeechBubble priceBubble;
        private int displayedPrice = -1;
        private int countdown;
        private double priceAnnouncementUntil;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 restScale;
        private double started;
        private double ends;
        private double nextBidComment;
        private double effectEnds;
        private bool mandatory;
        private bool cuePending;
        private int playerBids;
        private int aiBids;
        private Pose effect;
        private double bidImpactStarted = double.NegativeInfinity;
        private Vector3 priceRestScale;
        private Vector3 bubbleRestScale;
        private Color priceRestColor;
        private double gavelStarted = double.NegativeInfinity;
        private bool gavelSoundPending;

        internal Pose CurrentPose { get; private set; }
        internal string ResultLine { get; private set; }

        private void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            restScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (!auction)
                return;
            auction.OnStateChanged += ChangeState;
            auction.OnItemChanged += Introduce;
            auction.OnBidPlaced += ReactToBid;
            auction.OnAiDialogue += ReceiveResultLine;
            auction.OnClockChanged += UpdateCountdown;
            ChangeState(auction.State);
        }

        private void OnDisable()
        {
            if (auction)
            {
                auction.OnStateChanged -= ChangeState;
                auction.OnItemChanged -= Introduce;
                auction.OnBidPlaced -= ReactToBid;
                auction.OnAiDialogue -= ReceiveResultLine;
                auction.OnClockChanged -= UpdateCountdown;
            }
            ResetPresentation();
            bidImpactStarted = double.NegativeInfinity;
            RestorePrice();
            if (priceBubble)
                priceBubble.Hide();
            sprite.enabled = false;
        }

        private void OnDestroy()
        {
            if (bubble)
                Destroy(bubble.gameObject);
            if (priceBubble)
                Destroy(priceBubble.gameObject);
        }

        private void ChangeState(AuctionState state)
        {
            ResetPresentation();
            sprite.enabled = state is AuctionState.ItemReveal or AuctionState.Bidding
                or AuctionState.Sold or AuctionState.Unsold;
            if (!sprite.enabled && priceBubble)
                priceBubble.Hide();
            if (state != AuctionState.Bidding)
            {
                bidImpactStarted = double.NegativeInfinity;
                RestorePrice();
            }
            if (state == AuctionState.Idle || state == AuctionState.ItemReveal)
            {
                nextBidComment = 0;
                playerBids = 0;
                aiBids = 0;
            }

            if (state == AuctionState.Bidding)
                Speak("Ponuky, prosím! Kto sa odváži začať?", true);
            if (state == AuctionState.Sold && auction.HighestBidderId != BidderIds.Player)
                AnnounceResult(true, auction.SaleLineForAiWin);
        }

        private void Introduce(AuctionItem item) =>
            Speak($"Na javisku je {item.ItemName}! Dobre sa pozerajte.", true);

        private void ReactToBid(BidRecord bid)
        {
            double now = Time.unscaledTimeAsDouble;
            if (enableBidImpact)
            {
                bidImpactStarted = now;
                ShowPrice();
            }
            bool player = bid.bidderId == BidderIds.Player;
            int bidsFromSide = player ? ++playerBids : ++aiBids;
            if (bid.extendedTimer)
            {
                ResetCountdownTension();
                countdown = 0;
                priceAnnouncementUntil = auction.BiddingElapsed + 0.65;
                Speak($"Óóó, nová cena je ${bid.amount}!", true);
                ends = double.PositiveInfinity;
                return;
            }

            countdown = 0;
            UpdateCountdown();
            if (auction.RemainingTime <= AuctionManager.AntiSnipingTime ||
                bidsFromSide % 2 != 0 || now < nextBidComment || (now < ends && mandatory))
                return;

            Speak(player ? "Odvážna ponuka! Kto sa pridá?" : "Napätie stúpa. To sa mi páči!", false);
            nextBidComment = now + bidCooldown;
        }

        private void UpdateCountdown()
        {
            if (auction.RemainingTime > warningThreshold)
                ResetCountdownTension();

            if (auction.BiddingElapsed < priceAnnouncementUntil)
                return;
            priceAnnouncementUntil = 0;

            float remaining = auction.RemainingTime;
            int seconds = auction.State == AuctionState.Bidding && remaining > 0f &&
                remaining <= AuctionManager.AntiSnipingTime ? Mathf.CeilToInt(remaining) : 0;
            if (seconds == countdown)
                return;

            countdown = seconds;
            if (seconds == 0)
            {
                ClearBubble();
                return;
            }

            string call = seconds switch
            {
                3 => "po prvé",
                2 => "po druhé",
                _ => "po tretie"
            };
            Speak($"${auction.CurrentPrice} {call}!", true);
            ends = double.PositiveInfinity;
        }

        private void ShowPrice()
        {
            if (!auction || !dialogue)
                return;
            if (!priceBubble)
            {
                priceBubble = dialogue.CreateBubble("SALE Price");
                if (priceBubble)
                {
                    priceRestScale = priceBubble.Label.rectTransform.localScale;
                    bubbleRestScale = priceBubble.Rect.localScale;
                    priceRestColor = priceBubble.Label.color;
                }
            }
            if (!priceBubble)
                return;

            if (displayedPrice != auction.CurrentPrice)
            {
                displayedPrice = auction.CurrentPrice;
                priceBubble.SetText("$" + displayedPrice, 100, 155, 10, 1);
            }
            priceBubble.gameObject.SetActive(dialogue.PlaceSaleBubble(priceBubble, sprite, inFront: true));
            priceBubble.Group.alpha = 1f;
            if (double.IsNegativeInfinity(bidImpactStarted))
                return;
            float progress = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - bidImpactStarted) / bidImpactDuration));
            if (progress >= 1f || !enableBidImpact)
            {
                RestorePrice();
                bidImpactStarted = double.NegativeInfinity;
            }
            else
            {
                float pulse = Mathf.Sin(Mathf.PI * progress);
                priceBubble.Label.color = Color.Lerp(priceRestColor, bidFlashColor, Mathf.Clamp01(pulse * 1.8f));
                priceBubble.Label.rectTransform.localScale = priceRestScale * Mathf.Lerp(1f, pricePulseScale, pulse);
                priceBubble.Rect.localScale = bubbleRestScale * Mathf.Lerp(1f, bubblePulseScale, pulse);
            }
        }

        private void RestorePrice()
        {
            if (!priceBubble)
                return;
            priceBubble.Rect.localScale = bubbleRestScale;
            priceBubble.Label.rectTransform.localScale = priceRestScale;
            priceBubble.Label.color = priceRestColor;
        }

        private void ReceiveResultLine(string bidderId, string category, string line)
        {
            if (bidderId == "sale" && category is "SALE-01" or "SALE-02")
                AnnounceResult(category == "SALE-01", line);
        }

        internal void AnnounceResult(bool sold, string line)
        {
            ResultLine = line;
            if (!string.IsNullOrEmpty(line))
                Speak(line, true);
            effect = sold ? Pose.Excited : Pose.Disappointed;
            effectEnds = Time.unscaledTimeAsDouble + 0.6;
        }

        private void Speak(string line, bool required)
        {
            if (!dialogue)
                return;
            if (!bubble)
            {
                bubble = dialogue.CreateBubble("SALE Bubble");
                if (bubble)
                    dialogueRestScale = bubble.Label.rectTransform.localScale;
            }
            if (!bubble)
                return;

            bubble.Hide();
            bubble.SetSingleLine(line, 150, 440, 12, 34f);
            started = Time.unscaledTimeAsDouble;
            ends = started + (required ? 2.8 : 1.5);
            mandatory = required;
            cuePending = playDialogueCue && required;
        }

        internal void ClearBubble()
        {
            ResetCountdownTension();
            ends = 0;
            cuePending = false;
            if (bubble)
                bubble.Hide();
        }

        private void ResetPresentation()
        {
            StopGavelSlam();
            ClearBubble();
            countdown = 0;
            priceAnnouncementUntil = 0;
            effectEnds = 0;
            ResultLine = null;
            CurrentPose = Pose.Idle;
            RestorePose();
        }

        private void UpdateCountdownTension(bool speaking)
        {
            float remaining = auction.RemainingTime;
            if (!enableCountdownTension || warningThreshold <= 0f || !speaking || countdown <= 0 ||
                auction.State != AuctionState.Bidding || remaining > warningThreshold)
            {
                ResetCountdownTension();
                return;
            }

            warningActive = true;
            // Keep the pulse aligned to the auction clock, including after a late bid.
            float phase = Mathf.Repeat(remaining, warningPulseDuration) / warningPulseDuration;
            float pulse = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phase);
            bubble.Label.rectTransform.localScale = dialogueRestScale * Mathf.Lerp(1f, warningPulseScale, pulse);
        }

        private void ResetCountdownTension()
        {
            if (!warningActive)
                return;

            warningActive = false;
            if (bubble)
                bubble.Label.rectTransform.localScale = dialogueRestScale;
        }

        private void RestorePose()
        {
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
            transform.localScale = restScale;
        }

        internal void StartGavelSlam()
        {
            if (!isActiveAndEnabled || !enableGavelSlam)
                return;
            gavelStarted = Time.unscaledTimeAsDouble;
            gavelSoundPending = gavelImpactClip != null;
        }

        internal void StopGavelSlam()
        {
            if (double.IsNegativeInfinity(gavelStarted))
                return;
            gavelStarted = double.NegativeInfinity;
            gavelSoundPending = false;
            RestorePose();
        }

        private void LateUpdate()
        {
            if (!sprite.enabled)
                return;

            ShowPrice();
            double now = Time.unscaledTimeAsDouble;
            bool speaking = false;
            if (bubble && now < ends)
            {
                speaking = dialogue && dialogue.PlaceSaleBubble(bubble, sprite,
                    prioritize: countdown > 0 || priceAnnouncementUntil > 0);
                bubble.gameObject.SetActive(speaking);
                if (speaking)
                {
                    float appear = Mathf.Clamp01((float)(now - started) / 0.2f);
                    bubble.Group.alpha = appear * Mathf.Clamp01((float)(ends - now) / 0.15f);
                    bubble.Rect.localScale = Vector3.one * Mathf.Lerp(0.9f, 1, appear);
                    if (cuePending && appear > 0)
                    {
                        dialogue.PlaySaleCue();
                        cuePending = false;
                    }
                    if (!mandatory && !bubble.Positioned)
                    {
                        effect = Pose.Excited;
                        effectEnds = now + 0.6;
                    }
                    bubble.Positioned = true;
                }
                else if (!mandatory)
                    ClearBubble();
            }
            else if (bubble && bubble.gameObject.activeSelf)
                ClearBubble();

            UpdateCountdownTension(speaking);
            RestorePose();
            CurrentPose = now < effectEnds ? effect : speaking ? Pose.Speaking : Pose.Idle;
            float slam = (float)((now - gavelStarted) / gavelDuration);
            if (slam >= 0.45f && gavelSoundPending)
            {
                gavelSoundPending = false;
                ButtonClickAudio.Find(gameObject.scene)?.PlayGavelImpact(gavelImpactClip);
            }
            if (slam < 1f)
            {
                // Short anticipation, quick strike, then return to the resting pose.
                float gesture = slam < 0.2f ? Mathf.SmoothStep(0f, -0.35f, slam / 0.2f)
                    : slam < 0.45f ? Mathf.SmoothStep(-0.35f, 1f, (slam - 0.2f) / 0.25f)
                    : Mathf.SmoothStep(1f, 0f, (slam - 0.45f) / 0.55f);
                transform.localPosition += Vector3.down * (0.23f * gesture);
                transform.localRotation *= Quaternion.Euler(0, 0, -7f * gesture);
                return;
            }
            StopGavelSlam();
            float wave = Mathf.Sin((float)(now % 100) * 7);
            float impulse = Mathf.Sin(Mathf.PI * Mathf.Clamp01((float)(effectEnds - now) / 0.6f));
            switch (CurrentPose)
            {
                case Pose.Speaking:
                    transform.localScale = restScale * (1.025f + 0.015f * wave);
                    break;
                case Pose.Excited:
                    transform.localPosition += Vector3.up * (0.18f * impulse);
                    break;
                case Pose.Disappointed:
                    transform.localPosition -= Vector3.up * (0.1f * impulse);
                    transform.localRotation *= Quaternion.Euler(0, 0, 5 * impulse);
                    break;
                default:
                    transform.localPosition += Vector3.up * (0.02f * Mathf.Sin((float)(now % 100) * 2));
                    break;
            }
        }
    }
}
