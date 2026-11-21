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

        private SpriteRenderer sprite;
        private BidderSpeechBubble bubble;
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
            }
            ResetPresentation();
            sprite.enabled = false;
        }

        private void OnDestroy()
        {
            if (bubble)
                Destroy(bubble.gameObject);
        }

        private void ChangeState(AuctionState state)
        {
            ResetPresentation();
            sprite.enabled = state is AuctionState.ItemReveal or AuctionState.Bidding
                or AuctionState.Sold or AuctionState.Unsold;
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
            bool player = bid.bidderId == BidderIds.Player;
            int bidsFromSide = player ? ++playerBids : ++aiBids;
            if (bidsFromSide % 2 != 0 || now < nextBidComment || (now < ends && mandatory))
                return;

            Speak(player ? "Odvážna ponuka! Kto sa pridá?" : "Napätie stúpa. To sa mi páči!", false);
            nextBidComment = now + bidCooldown;
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
                bubble = dialogue.CreateBubble("SALE Bubble");
            if (!bubble)
                return;

            bubble.Hide();
            bubble.SetText(line, 180, 260, 12, 5);
            started = Time.unscaledTimeAsDouble;
            ends = started + (required ? 2.8 : 1.5);
            mandatory = required;
            cuePending = playDialogueCue && required;
        }

        internal void ClearBubble()
        {
            ends = 0;
            cuePending = false;
            if (bubble)
                bubble.Hide();
        }

        private void ResetPresentation()
        {
            ClearBubble();
            effectEnds = 0;
            ResultLine = null;
            CurrentPose = Pose.Idle;
            RestorePose();
        }

        private void RestorePose()
        {
            transform.SetLocalPositionAndRotation(restPosition, restRotation);
            transform.localScale = restScale;
        }

        private void LateUpdate()
        {
            if (!sprite.enabled)
                return;

            double now = Time.unscaledTimeAsDouble;
            bool speaking = false;
            if (bubble && now < ends)
            {
                speaking = dialogue && dialogue.PlaceSaleBubble(bubble, sprite);
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

            RestorePose();
            CurrentPose = now < effectEnds ? effect : speaking ? Pose.Speaking : Pose.Idle;
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
