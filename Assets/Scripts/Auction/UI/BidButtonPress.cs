using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    internal sealed class BidButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private Button button;
        private Graphic graphic;
        private Transform visual;
        private Vector3 restScale;
        private Vector4 restPadding;
        private float pressScale;
        private float releaseScale;
        private float duration;
        private int? pointer;
        private double releasedAt = double.NegativeInfinity;

        internal static BidButtonPress Bind(Button button, float pressScale = .94f, float releaseScale = 1.04f,
            float duration = .18f, Transform visual = null)
        {
            var effect = button.gameObject.AddComponent<BidButtonPress>();
            effect.button = button;
            effect.graphic = button.targetGraphic;
            effect.visual = visual ? visual : button.transform;
            effect.restScale = effect.visual.localScale;
            if (effect.graphic)
                effect.restPadding = effect.graphic.raycastPadding;
            effect.pressScale = pressScale;
            effect.releaseScale = releaseScale;
            effect.duration = duration;
            return effect;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (pointer.HasValue || eventData.button != PointerEventData.InputButton.Left || !CanAnimate())
                return;
            pointer = eventData.pointerId;
            releasedAt = double.NegativeInfinity;
            SetScale(pressScale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || pointer != eventData.pointerId)
                return;
            pointer = null;
            releasedAt = Time.unscaledTimeAsDouble;
        }

        private void LateUpdate()
        {
            if (!pointer.HasValue && double.IsNegativeInfinity(releasedAt))
                return;
            if (!CanAnimate())
            {
                ResetPress();
                return;
            }
            if (pointer.HasValue)
                return;

            float progress = (float)((Time.unscaledTimeAsDouble - releasedAt) / duration);
            if (progress >= 1f)
                ResetPress();
            else if (progress < 0.45f)
                SetScale(Mathf.SmoothStep(pressScale, releaseScale, progress / 0.45f));
            else
                SetScale(Mathf.SmoothStep(releaseScale, 1f, (progress - 0.45f) / 0.55f));
        }

        private bool CanAnimate() => isActiveAndEnabled && button.isActiveAndEnabled &&
            button.IsInteractable() && graphic && graphic.canvasRenderer.GetInheritedAlpha() > 0f;

        private void SetScale(float scale)
        {
            if (!visual)
                return;
            visual.localScale = restScale * scale;
            if (!graphic || graphic.transform != visual)
                return;

            // Counter-scale the raycast rectangle so pressing near an edge still clicks.
            Rect rect = graphic.rectTransform.rect;
            var edges = new Vector4(-rect.xMin, -rect.yMin, rect.xMax, rect.yMax);
            graphic.raycastPadding = (restPadding + edges * (scale - 1f)) / scale;
        }

        internal void ResetPress()
        {
            pointer = null;
            releasedAt = double.NegativeInfinity;
            SetScale(1f);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ResetPress();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                ResetPress();
        }

        private void OnDisable() => ResetPress();
    }
}
