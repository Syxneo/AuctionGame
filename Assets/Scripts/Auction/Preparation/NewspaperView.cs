using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class NewspaperView : MonoBehaviour
    {
        private Button closeButton;
        private ScrollRect scroll;
        private GameObject previousSelection;
        private CanvasGroup coveredControls;
        private bool controlsBlocked;
        private bool previousRaycasts;
        private bool previousInteraction;

        public bool IsOpen => closeButton && gameObject.activeSelf;

        public void Initialize(MvpAuctionLevel level, CanvasGroup controls = null)
        {
            if (closeButton || !level)
                return;

            coveredControls = controls;
            PreparationUi.Surface((RectTransform)transform, new Color(0, 0, 0, 0.85f));
            var page = PreparationUi.Rect("Page", transform, Vector2.zero, Vector2.one,
                new Vector2(24, 24), new Vector2(-24, -24));
            PreparationUi.Surface(page, PreparationUi.Paper);

            var header = PreparationUi.Rect("Title", page, Vector2.up, Vector2.one,
                new Vector2(24, -64), new Vector2(-205, -12));
            PreparationUi.Label(header, "Newspaper", 32, PreparationUi.Ink, FontStyles.Bold);
            closeButton = PreparationUi.Button(PreparationUi.Rect("Close Newspaper", page, Vector2.one,
                Vector2.one, new Vector2(-190, -62), new Vector2(-20, -14)), "Close Newspaper");
            closeButton.onClick.AddListener(Close);

            var viewport = PreparationUi.Rect("Articles", page, Vector2.zero, Vector2.one,
                new Vector2(24, 16), new Vector2(-24, -72));
            viewport.gameObject.AddComponent<RectMask2D>();
            PreparationUi.Surface(viewport, PreparationUi.Paper);
            var content = PreparationUi.Rect("Article content", viewport, Vector2.up, Vector2.one,
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 10, 6, 20);
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;

            AddSection(level, NewspaperImportance.MainArticle, "Main articles");
            AddSection(level, NewspaperImportance.ShortReport, "Short reports");
            gameObject.SetActive(false);
        }

        private void AddSection(MvpAuctionLevel level, NewspaperImportance importance, string title)
        {
            AddText(title, 18, FontStyles.Bold);
            foreach (NewspaperClue clue in level.Clues)
            {
                if (clue.Importance != importance)
                    continue;

                AddText(clue.Headline, 25, FontStyles.Bold);
                AddText(clue.Body, 21, FontStyles.Normal);
            }
        }

        private void AddText(string value, int size, FontStyles style)
        {
            PreparationUi.Label(PreparationUi.Fill("Article text", scroll.content), value, size,
                PreparationUi.Ink, style).overflowMode = TextOverflowModes.Overflow;
        }

        public void Open()
        {
            if (IsOpen || !closeButton)
                return;

            var events = EventSystem.current;
            previousSelection = events ? events.currentSelectedGameObject : null;
            if (coveredControls)
            {
                previousRaycasts = coveredControls.blocksRaycasts;
                previousInteraction = coveredControls.interactable;
                coveredControls.blocksRaycasts = false;
                coveredControls.interactable = false;
                controlsBlocked = true;
            }
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
            if (events)
                events.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            RestoreControls();
            gameObject.SetActive(false);
            var events = EventSystem.current;
            if (events)
                events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy
                    ? previousSelection : null);

            previousSelection = null;
        }

        private void OnDestroy() => RestoreControls();

        private void RestoreControls()
        {
            if (!controlsBlocked || !coveredControls)
                return;

            coveredControls.blocksRaycasts = previousRaycasts;
            coveredControls.interactable = previousInteraction;
            controlsBlocked = false;
        }
    }
}
