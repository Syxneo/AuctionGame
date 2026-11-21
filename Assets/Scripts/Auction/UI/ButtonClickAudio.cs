using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    public sealed class ButtonClickAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource effects;
        [SerializeField] private AudioClip click;
        [SerializeField, Range(0, 1)] private float uiVolume = 1f;
        [SerializeField, Range(0, 1)] private float effectsVolume = 1f;
        [SerializeField] private AuctionManager auction;
        [SerializeField] private SceneIntroOutro stage;
        [SerializeField] private AudioClip newsTransition;
        [SerializeField] private AudioClip successfulBid;
        [SerializeField] private AudioClip rejectedBid;
        [SerializeField] private AudioClip sold;
        [SerializeField] private AudioClip unsold;
        [SerializeField] private AudioClip curtain;
        [SerializeField] private AudioClip success;
        [SerializeField] private AudioClip gameOver;

        private bool paused;
        private bool focused = true;
        private double nextClick;
        private double nextBid;

        internal static ButtonClickAudio Find(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out ButtonClickAudio audio))
                    return audio;

            return null;
        }

        internal static void Bind(Button button)
        {
            if (!button)
                return;

            var audio = Find(button.gameObject.scene);
            if (audio)
            {
                button.onClick.RemoveListener(audio.PlayClick);
                button.onClick.AddListener(audio.PlayClick);
            }
        }

        private void OnEnable()
        {
            focused = Application.isFocused;
            if (auction)
            {
                auction.OnBidPlaced += BidPlaced;
                auction.OnPlayerBidRejected += BidRejected;
                auction.OnAuctionEnded += AuctionEnded;
                auction.OnStateChanged += StateChanged;
            }
            if (stage)
                stage.CurtainMovementStarted += CurtainStarted;
        }

        private void OnDisable()
        {
            if (auction)
            {
                auction.OnBidPlaced -= BidPlaced;
                auction.OnPlayerBidRejected -= BidRejected;
                auction.OnAuctionEnded -= AuctionEnded;
                auction.OnStateChanged -= StateChanged;
            }
            if (stage)
                stage.CurtainMovementStarted -= CurtainStarted;
            ResetPlayback();
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (value)
                ResetPlayback();
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value)
                ResetPlayback();
        }

        private void StateChanged(AuctionState state)
        {
            if (state == AuctionState.Idle)
                ResetPlayback();
        }

        private void ResetPlayback()
        {
            if (effects)
                effects.Stop();
            nextClick = nextBid = 0;
        }

        private void PlayClick() => PlayLimited(click, uiVolume, ref nextClick, .08);
        private void BidPlaced(BidRecord _) => PlayBid(successfulBid);
        private void BidRejected() => PlayBid(rejectedBid);
        private void PlayBid(AudioClip clip) =>
            PlayLimited(clip, effectsVolume, ref nextBid, clip ? System.Math.Max(.12, clip.length) : 0);
        private void AuctionEnded(AuctionResult result) => Play(result.sold ? sold : unsold);
        internal void PlayGavelImpact(AudioClip clip)
        {
            if (!sold)
                Play(clip);
        }
        private void CurtainStarted() => Play(curtain);
        internal void NewsEntered() => Play(newsTransition);
        internal void ReportOpened(bool succeeded) => Play(succeeded ? success : gameOver);

        private void PlayLimited(AudioClip clip, float volume, ref double next, double interval)
        {
            double now = AudioSettings.dspTime;
            if (now >= next && CanPlay(clip))
            {
                effects.PlayOneShot(clip, volume);
                next = now + interval;
            }
        }

        private bool CanPlay(AudioClip clip) => isActiveAndEnabled && !paused && focused && effects &&
            effects.isActiveAndEnabled && clip;

        private void Play(AudioClip clip)
        {
            if (CanPlay(clip))
                effects.PlayOneShot(clip, effectsVolume);
        }
    }
}
