using System;
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

        [Header("Auction Opening")]
        [SerializeField] private Transform openingTarget;
        [SerializeField] private Vector3 openingCameraOffset = new(0f, 6.8f, 11f);
        [SerializeField] private Vector3 openingLookOffset = new(0f, 1f, 0f);
        [SerializeField, Range(1, 179)] private float openingFieldOfView = 48f;
        [SerializeField, Min(0)] private float openingDuration = 2f;

        [Header("Auction Result")]
        [SerializeField] private Vector3 closeUpOffset = new(0f, 6.8f, 11f);
        [SerializeField] private Vector3 closeUpLookOffset = new(0f, 1f, 0f);
        [SerializeField, Range(1, 179)] private float closeUpFieldOfView = 48f;
        [SerializeField, Min(0)] private float approachDuration = 0.7f;
        [SerializeField, Min(0)] private float curtainCloseDelay = 0.5f;
        [SerializeField, Min(0)] private float curtainCloseDuration = 0.9f;
        [SerializeField, Min(0)] private float resultHoldDuration = 1.2f;
        [SerializeField, Min(0)] private float reopenDuration = 1f;

        [Header("Easing")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsPlaying { get; private set; }
        internal bool IsStageRevealed => curtainsOpen &&
            (resultFinished == null || resultElapsed < curtainCloseDelay);
        internal event Action CurtainMovementStarted;
        internal bool OpeningItemVisible => !openingPrepared || openingElapsed > 0f;
        internal float? CurtainRevealProgress
        {
            get
            {
                if (openingFinished != null && openingDuration > 0f)
                    return Mathf.Clamp01(openingElapsed / openingDuration);
                if (resultFinished != null && resultConfirmed && reopenDuration > 0f && resultElapsed >= ResultReopenAt)
                    return Mathf.Clamp01((resultElapsed - ResultReopenAt) / reopenDuration);
                return null;
            }
        }

        private float ResultClosedAt => Mathf.Max(approachDuration, curtainCloseDelay + curtainCloseDuration);
        private float ResultReopenAt => ResultClosedAt + resultHoldDuration;

        private bool curtainsOpen;
        private bool openingPrepared;
        private double openingStartedAt;
        private float openingElapsed;
        private Action openingFinished;
        private Camera stageCamera;
        private Pose startLocalPose;
        private Pose startWorldPose;
        private Pose closeUpPose;
        private float startFieldOfView;
        private Vector3 startLeftCurtain;
        private Vector3 startRightCurtain;
        private float resultElapsed;
        private bool resultConfirmed;
        private Action<bool> setResultVisible;
        private Action resultFinished;

        private void Start()
        {
            if (IsPlaying)
                return;

            if (playIntroOnStart)
                PlayIntro();
            else
            {
                ApplyPose(1f);
                curtainsOpen = true;
            }
        }

        /// <summary>Camera pans out from cameraZStart to cameraZEnd while the curtains open.</summary>
        public void PlayIntro() => StartCoroutine(PlayRoutine(0f, 1f, introDelay, introDuration, null));

        /// <summary>Curtains close and the camera returns to cameraZStart. Optionally loads another scene.</summary>
        public void PlayOutro() => StartCoroutine(PlayRoutine(1f, 0f, 0f, outroDuration, sceneToLoadAfterOutro));

        /// <summary>Jumps straight to the end pose (open curtains, camera pulled back).</summary>
        public void SkipToEnd()
        {
            if (openingPrepared)
            {
                FinishOpening();
                return;
            }

            if (resultFinished != null)
            {
                FinishResult();
                return;
            }

            StopAllCoroutines();
            IsPlaying = false;
            ApplyPose(1f);
            curtainsOpen = true;
        }

        internal bool PlayResult(Vector3 podiumPosition, Action<bool> setVisible, Action onFinished)
        {
            if (openingPrepared || resultFinished != null || !CanAnimateStage())
                return false;

            // A result reached during the intro settles the stage at its gameplay pose first.
            if (IsPlaying)
                SkipToEnd();

            CaptureStage(podiumPosition, closeUpOffset, closeUpLookOffset);
            resultElapsed = 0f;
            resultConfirmed = false;
            setResultVisible = setVisible;
            resultFinished = onFinished;
            IsPlaying = true;
            return true;
        }

        internal void CancelResult() => FinishResult();

        internal void ConfirmResult() => resultConfirmed = true;

        private void Update() => AdvanceResultMotion(Time.deltaTime);

        internal bool PrepareOpening()
        {
            if (!openingTarget || openingPrepared || resultFinished != null || !CanAnimateStage())
                return false;

            if (IsPlaying)
                SkipToEnd();

            CaptureStage(openingTarget.position, openingCameraOffset, openingLookOffset);
            openingPrepared = true;
            openingElapsed = 0f;
            IsPlaying = true;
            curtainsOpen = false;
            ApplyStageMotion(1f, 0f, openingFieldOfView);
            return true;
        }

        internal bool PlayOpening(Action onFinished)
        {
            if (!openingPrepared || openingFinished != null)
                return false;

            openingFinished = onFinished;
            openingStartedAt = Time.realtimeSinceStartupAsDouble;
            CurtainMovementStarted?.Invoke();
            return true;
        }

        private void LateUpdate()
        {
            if (openingFinished == null)
                return;

            openingElapsed = (float)(Time.realtimeSinceStartupAsDouble - openingStartedAt);
            if (openingElapsed >= openingDuration)
            {
                FinishOpening();
                return;
            }

            float open = EasedProgress(openingElapsed, openingDuration);
            ApplyStageMotion(1f - open, open, openingFieldOfView);
        }

        private void FinishOpening()
        {
            Action finished = openingFinished;
            openingFinished = null;
            openingPrepared = false;
            curtainsOpen = true;
            RestoreStage();
            finished?.Invoke();
        }

        private bool CanAnimateStage() => isActiveAndEnabled && cameraTransform &&
            cameraTransform.TryGetComponent(out stageCamera) && leftCurtain && rightCurtain;

        private void CaptureStage(Vector3 target, Vector3 cameraOffset, Vector3 lookOffset)
        {
            startLocalPose = new Pose(cameraTransform.localPosition, cameraTransform.localRotation);
            startWorldPose = new Pose(cameraTransform.position, cameraTransform.rotation);
            startFieldOfView = stageCamera.fieldOfView;
            startLeftCurtain = leftCurtain.localPosition;
            startRightCurtain = rightCurtain.localPosition;
            Vector3 position = target + cameraOffset;
            Vector3 aim = target + lookOffset - position;
            closeUpPose = new Pose(position, aim.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(aim) : startWorldPose.rotation);
        }

        private void ApplyStageMotion(float cameraClose, float curtainOpen, float fieldOfView)
        {
            cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(startWorldPose.position, closeUpPose.position, cameraClose),
                Quaternion.Slerp(startWorldPose.rotation, closeUpPose.rotation, cameraClose));
            stageCamera.fieldOfView = Mathf.Lerp(startFieldOfView, fieldOfView, cameraClose);
            ApplyCurtains(curtainOpen, startLeftCurtain.x, startRightCurtain.x);
        }

        private void AdvanceResultMotion(float deltaTime)
        {
            if (resultFinished == null)
                return;

            float closedAt = ResultClosedAt;
            float reopenAt = ResultReopenAt;
            float previousElapsed = resultElapsed;
            resultElapsed = resultConfirmed
                ? resultElapsed + deltaTime
                : Mathf.Min(resultElapsed + deltaTime, reopenAt);
            if (curtainCloseDuration > 0 && previousElapsed <= curtainCloseDelay && resultElapsed > curtainCloseDelay)
                CurtainMovementStarted?.Invoke();
            if (reopenDuration > 0 && previousElapsed <= reopenAt && resultElapsed > reopenAt)
                CurtainMovementStarted?.Invoke();

            if (resultElapsed >= reopenAt + reopenDuration)
            {
                FinishResult();
                return;
            }

            float cameraClose;
            float curtainOpen;
            if (!resultConfirmed || resultElapsed < reopenAt)
            {
                cameraClose = EasedProgress(resultElapsed, approachDuration);
                curtainOpen = 1f - EasedProgress(resultElapsed - curtainCloseDelay, curtainCloseDuration);
            }
            else
            {
                curtainOpen = EasedProgress(resultElapsed - reopenAt, reopenDuration);
                cameraClose = 1f - curtainOpen;
            }

            ApplyStageMotion(cameraClose, curtainOpen, closeUpFieldOfView);
            setResultVisible(resultElapsed >= closedAt && (!resultConfirmed || resultElapsed < reopenAt));
        }

        private float EasedProgress(float elapsed, float duration)
        {
            if (elapsed < 0f)
                return 0f;
            if (elapsed >= duration)
                return 1f;
            return ease.Evaluate(elapsed / duration);
        }

        private void FinishResult()
        {
            if (resultFinished == null)
                return;

            Action finished = resultFinished;
            resultFinished = null;
            setResultVisible = null;
            RestoreStage();
            finished();
        }

        private void RestoreStage()
        {
            IsPlaying = false;
            if (cameraTransform)
                cameraTransform.SetLocalPositionAndRotation(startLocalPose.position, startLocalPose.rotation);
            if (stageCamera)
                stageCamera.fieldOfView = startFieldOfView;
            if (leftCurtain)
                leftCurtain.localPosition = startLeftCurtain;
            if (rightCurtain)
                rightCurtain.localPosition = startRightCurtain;
        }

        private void OnDisable()
        {
            if (openingPrepared)
                FinishOpening();
            else if (resultFinished != null)
                FinishResult();
            else if (IsPlaying)
                SkipToEnd();
        }

        private IEnumerator PlayRoutine(float from, float to, float delay, float duration, string sceneAfter)
        {
            if (IsPlaying) yield break;
            IsPlaying = true;
            curtainsOpen = false;

            ApplyPose(from);
            if (delay > 0f) yield return new WaitForSeconds(delay);

            if (duration > 0 && (leftCurtain || rightCurtain))
                CurtainMovementStarted?.Invoke();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ApplyPose(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            ApplyPose(to);
            curtainsOpen = to == 1f;

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
            ApplyCurtains(e, curtainOpenX, -curtainOpenX);
        }

        private void ApplyCurtains(float open, float leftOpenX, float rightOpenX)
        {
            if (leftCurtain != null)
            {
                Vector3 p = leftCurtain.localPosition;
                p.x = Mathf.Lerp(curtainClosedX, leftOpenX, open);
                leftCurtain.localPosition = p;
            }
            if (rightCurtain != null)
            {
                Vector3 p = rightCurtain.localPosition;
                p.x = Mathf.Lerp(-curtainClosedX, rightOpenX, open);
                rightCurtain.localPosition = p;
            }
        }
    }
}
