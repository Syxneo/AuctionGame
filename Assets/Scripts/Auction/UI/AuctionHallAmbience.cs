using UnityEngine;

namespace AuctionGame.Auction
{
    public sealed class AuctionHallAmbience : MonoBehaviour
    {
        [SerializeField] private AuctionManager auction;
        [SerializeField] private AudioSource[] tracks = System.Array.Empty<AudioSource>();
        private bool playing;

        private void OnEnable()
        {
            if (!auction)
                return;

            auction.OnStateChanged += UpdatePlayback;
            UpdatePlayback(auction.State);
        }

        private void OnDisable()
        {
            if (auction)
                auction.OnStateChanged -= UpdatePlayback;
            UpdatePlayback(AuctionState.Idle);
        }

        private void UpdatePlayback(AuctionState state)
        {
            bool shouldPlay = state is not (AuctionState.Idle or AuctionState.Finished);
            if (playing == shouldPlay)
                return;

            playing = shouldPlay;
            foreach (AudioSource track in tracks)
            {
                if (!track)
                    continue;

                if (playing)
                    track.Play();
                else
                    track.Stop();
            }
        }
    }
}
