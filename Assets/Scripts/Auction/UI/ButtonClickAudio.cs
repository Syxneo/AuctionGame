using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    public sealed class ButtonClickAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource effects;
        [SerializeField] private AudioClip click;

        internal static void Bind(Button button)
        {
            if (!button)
                return;

            foreach (GameObject root in button.gameObject.scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out ButtonClickAudio audio))
                {
                    button.onClick.AddListener(audio.Play);
                    return;
                }
            }
        }

        private void Play()
        {
            if (isActiveAndEnabled && effects && click)
                effects.PlayOneShot(click);
        }
    }
}
