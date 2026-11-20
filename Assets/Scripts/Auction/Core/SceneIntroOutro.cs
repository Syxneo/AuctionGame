using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuctionGame.Auction
{
    /// <summary>
    /// Curtain-reveal intro on scene load and a reverse outro on demand.
    /// Camera Z and curtain X endpoints, durations, delay and easing are all
    /// tweakable in the Inspector. Call <see cref="PlayOutro"/> from UI or game
    /// code when leaving the scene.
    /// </summary>
    public sealed class SceneIntroOutro : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform leftCurtain;
        [SerializeField] private Transform rightCurtain;

        [Header("Intro")]
        [SerializeField] private bool playIntroOnStart = true;
        [SerializeField] private float introDelay = 0.5f;
        [SerializeField] private float introDuration = 4f;

        [Header("Outro")]
        [SerializeField] private float outroDuration = 3f;
        [SerializeField] private string sceneToLoadAfterOutro = "";

        [Header("Camera Z (start -> end)")]
        [SerializeField] private float cameraZStart = 4.5f;
        [SerializeField] private float cameraZEnd = 25f;

        [Header("Curtain X (closed -> open)")]
        [SerializeField] private float curtainClosedX = 2f;
        [SerializeField] private float curtainOpenX = 8f;

        [Header("Easing")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsPlaying { get; private set; }

        private void Start()
        {
            if (playIntroOnStart)
                PlayIntro();
            else
                ApplyPose(1f);
        }

        /// <summary>Camera pans out from cameraZStart to cameraZEnd while the curtains open.</summary>
        public void PlayIntro() => StartCoroutine(PlayRoutine(0f, 1f, introDelay, introDuration, null));

        /// <summary>Curtains close and the camera returns to cameraZStart. Optionally loads another scene.</summary>
        public void PlayOutro() => StartCoroutine(PlayRoutine(1f, 0f, 0f, outroDuration, sceneToLoadAfterOutro));

        /// <summary>Jumps straight to the end pose (open curtains, camera pulled back).</summary>
        public void SkipToEnd()
        {
            StopAllCoroutines();
            IsPlaying = false;
            ApplyPose(1f);
        }

        private IEnumerator PlayRoutine(float from, float to, float delay, float duration, string sceneAfter)
        {
            if (IsPlaying) yield break;
            IsPlaying = true;

            ApplyPose(from);
            if (delay > 0f) yield return new WaitForSeconds(delay);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ApplyPose(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            ApplyPose(to);

            IsPlaying = false;
            if (!string.IsNullOrEmpty(sceneAfter))
                SceneManager.LoadScene(sceneAfter);
        }

        private void ApplyPose(float t)
        {
            float e = ease.Evaluate(Mathf.Clamp01(t));

            if (cameraTransform != null)
            {
                Vector3 p = cameraTransform.position;
                p.z = Mathf.Lerp(cameraZStart, cameraZEnd, e);
                cameraTransform.position = p;
            }
            if (leftCurtain != null)
            {
                Vector3 p = leftCurtain.localPosition;
                p.x = Mathf.Lerp(curtainClosedX, curtainOpenX, e);
                leftCurtain.localPosition = p;
            }
            if (rightCurtain != null)
            {
                Vector3 p = rightCurtain.localPosition;
                p.x = Mathf.Lerp(-curtainClosedX, -curtainOpenX, e);
                rightCurtain.localPosition = p;
            }
        }
    }
}
