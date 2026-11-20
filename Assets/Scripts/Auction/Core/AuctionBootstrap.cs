using UnityEngine;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(AuctionManager))]
    public sealed class AuctionBootstrap : MonoBehaviour
    {
        private void Start()
        {
            if (!GetComponent<AuctionManager>().StartAuction())
                Debug.LogError("Could not start auction. Check the item definitions and unique IDs.", this);
        }
    }
}
