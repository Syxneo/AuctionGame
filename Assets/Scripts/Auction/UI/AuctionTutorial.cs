using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    internal sealed class AuctionTutorial : MonoBehaviour
    {
        private const float CardHeight = .35f;
        private const float CardLeft = .04f;
        private const float CardRight = .96f;
        private const float CardCenter = (1f - CardHeight) / 2f;
        private static readonly float[] CardPositions = { CardCenter, .96f - CardHeight, .04f };
        private RectTransform overlay;
        private RectTransform safe;
        private RectTransform card;
        private TMP_Text title;
        private TMP_Text body;
        private Button next;
        private TMP_Text nextLabel;
        private AuctionUI hud;
        private RectTransform[] bidTargets;
        private Camera hallCamera;
        private SaleAuctioneer host;
        private SpriteRenderer auctioneer;
        private SpriteRenderer item;
        private SpriteRenderer[] bidders = Array.Empty<SpriteRenderer>();
        private CanvasGroup[] highlights;
        private readonly Vector3[] corners = new Vector3[4];
        private int step;
        private int lastTap;
        private float highlightStarted;
        private Action completed;

        internal static void Show(RectTransform parent, Sprite parchment, Sprite buttonParchment, TMP_FontAsset font,
            AuctionUI hud, AuctionManager auction, Action completed)
        {
            var root = PreparationUi.Fill("Day 1 Auction Tutorial", parent);
            var tutorial = root.gameObject.AddComponent<AuctionTutorial>();
            tutorial.Build(root, parchment, buttonParchment, font, hud, auction, completed);
        }

        private void Build(RectTransform root, Sprite parchment, Sprite buttonParchment, TMP_FontAsset font,
            AuctionUI auctionHud, AuctionManager auction, Action onCompleted)
        {
            overlay = root;
            hud = auctionHud;
            completed = onCompleted;
            lastTap = Time.frameCount;
            PreparationUi.Surface(root, new Color(.08f, .05f, .03f, .28f));
            safe = PreparationUi.Fill("Tutorial Safe Area", root);
            if (hud)
            {
                bidTargets = hud.BidTutorialTargets;
                hallCamera = Camera.main;
                host = FindObjectsByType<SaleAuctioneer>(FindObjectsSortMode.None)
                    .FirstOrDefault(presenter => presenter.gameObject.scene == auction.gameObject.scene);
                var podium = FindObjectsByType<AuctionItemSpritePresenter>(FindObjectsSortMode.None)
                    .FirstOrDefault(presenter => presenter.gameObject.scene == auction.gameObject.scene);
                var opponents = FindObjectsByType<BidderDialoguePresenter>(FindObjectsSortMode.None)
                    .FirstOrDefault(presenter => presenter.gameObject.scene == auction.gameObject.scene);
                auctioneer = host ? host.GetComponent<SpriteRenderer>() : null;
                if (host)
                    host.ShowTutorialPrice(true);
                item = podium ? podium.GetComponent<SpriteRenderer>() : null;
                if (opponents)
                    bidders = opponents.TutorialBidders.ToArray();
                highlights = new CanvasGroup[Mathf.Max(bidTargets.Length, 2 + bidders.Length)];
                for (int i = 0; i < highlights.Length; i++)
                    highlights[i] = CreateHighlight();
            }

            card = Box("Tutorial Parchment", safe, CardLeft, CardCenter, CardRight, CardCenter + CardHeight);
            var paper = PreparationUi.Surface(card, parchment ? Color.white : PreparationUi.Paper);
            paper.sprite = parchment;
            title = PreparationUi.Label(Box("Title", card, .06f, .77f, .94f, .94f), "", 48,
                PreparationUi.Ink, FontStyles.Bold);
            body = PreparationUi.Label(Box("Explanation", card, .075f, .28f, .925f, .77f), "", 40,
                PreparationUi.Ink);
            body.enableAutoSizing = true;
            body.fontSizeMin = 32;
            body.fontSizeMax = 40;
            next = PreparationUi.ParchmentButton(Box("Next Tutorial Step", card, .23f, .065f, .77f, .265f),
                "ĎALEJ", buttonParchment, font, 40);
            nextLabel = next.GetComponentInChildren<TMP_Text>();
            foreach (var label in new[] { title, body })
            {
                label.font = font;
                label.alignment = TextAlignmentOptions.Center;
            }
            next.onClick.AddListener(Advance);
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(next.gameObject);
            ShowStep();
        }

        private void Advance()
        {
            if (completed == null || lastTap == Time.frameCount)
                return;
            lastTap = Time.frameCount;
            if (step < 2)
            {
                step++;
                ShowStep();
                return;
            }

            Action close = completed;
            completed = null;
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
            close();
        }

        private void OnDisable()
        {
            if (host)
                host.ShowTutorialPrice(false);
        }

        private void ShowStep()
        {
            highlightStarted = Time.unscaledTime;
            (title.text, body.text) = step switch
            {
                0 => ("PRIHADZOVANIE",
                    $"Prvý príhoz je ${AuctionManager.MinimumOpeningBid}. Ďalšími tlačidlami zvyšuješ aktuálnu cenu."),
                1 => ("ČAS DRAŽBY",
                    $"Na prihadzovanie máš od začiatku {AuctionManager.InitialBiddingTime:0} s. Príhoz v posledných {AuctionManager.AntiSnipingTime:0} sekundách vráti odpočet najviac na {AuctionManager.AntiSnipingTime:0} s. Takto sa dražba môže predĺžiť spolu o ďalších {AuctionManager.MaximumBiddingDuration - AuctionManager.InitialBiddingTime:0} s, teda najviac na {AuctionManager.MaximumBiddingDuration:0} s."),
                _ => ("NAKUPUJ ROZUMNE",
                    "Sleduj rozpočet a indície. Neplať viac, než podľa teba predmet stojí.")
            };
            nextLabel.text = step == 2 ? "ROZUMIEM" : "ĎALEJ";
        }

        private void LateUpdate()
        {
            if (!hud || Screen.width <= 0 || Screen.height <= 0)
                return;
            Vector2 screen = new(Screen.width, Screen.height);
            safe.anchorMin = Screen.safeArea.min / screen;
            safe.anchorMax = Screen.safeArea.max / screen;
            float pulse = .74f + .26f * Mathf.Cos((Time.unscaledTime - highlightStarted) * 5f);
            for (int i = 0; i < highlights.Length; i++)
            {
                Rect target = step switch
                {
                    0 when i == 0 && host && host.TutorialPrice && host.TutorialPrice.gameObject.activeInHierarchy
                        => ScreenBounds(host.TutorialPrice),
                    0 when i < bidTargets.Length => ScreenBounds(bidTargets[i]),
                    1 when i == 0 => hud.TimerTutorialTarget
                        ? ScreenBounds(hud.TimerTutorialTarget) : ScreenBounds(auctioneer),
                    2 when i == 0 => ScreenBounds(hud.BudgetTutorialTarget),
                    2 when i == 1 => ScreenBounds(item),
                    2 when i - 2 >= 0 && i - 2 < bidders.Length => ScreenBounds(bidders[i - 2]),
                    _ => default
                };
                highlights[i].alpha = pulse;
                PlaceHighlight((RectTransform)highlights[i].transform, target, screen);
            }

            // Keep the card away from the current step's targets.
            float bestOverlap = float.PositiveInfinity;
            float bestBottom = CardCenter;
            foreach (float bottom in CardPositions)
            {
                var band = Rect.MinMaxRect(CardLeft, bottom, CardRight, bottom + CardHeight);
                float overlap = 0;
                foreach (var highlight in highlights)
                {
                    if (!highlight.gameObject.activeSelf)
                        continue;
                    var rect = (RectTransform)highlight.transform;
                    overlap += Mathf.Max(0, Mathf.Min(band.xMax, rect.anchorMax.x) -
                        Mathf.Max(band.xMin, rect.anchorMin.x)) *
                        Mathf.Max(0, Mathf.Min(band.yMax, rect.anchorMax.y) -
                        Mathf.Max(band.yMin, rect.anchorMin.y));
                }
                if (overlap < bestOverlap)
                {
                    bestOverlap = overlap;
                    bestBottom = bottom;
                }
            }
            card.anchorMin = new Vector2(CardLeft, bestBottom);
            card.anchorMax = new Vector2(CardRight, bestBottom + CardHeight);
        }

        private Rect ScreenBounds(RectTransform target)
        {
            if (!target || !target.gameObject.activeInHierarchy)
                return default;
            target.GetWorldCorners(corners);
            var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Rect ScreenBounds(SpriteRenderer target)
        {
            if (!target || !target.enabled || !target.sprite || !hallCamera)
                return default;
            var bounds = target.bounds;
            Vector3 min = hallCamera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
            Vector3 max = hallCamera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void PlaceHighlight(RectTransform highlight, Rect bounds, Vector2 screen)
        {
            highlight.gameObject.SetActive(bounds.width > 0 && bounds.height > 0);
            if (!highlight.gameObject.activeSelf)
                return;
            Rect area = Screen.safeArea;
            var padding = Vector2.one * (screen.x / 120f);
            highlight.anchorMin = (bounds.min - area.min - padding) / area.size;
            highlight.anchorMax = (bounds.max - area.min + padding) / area.size;
        }

        private CanvasGroup CreateHighlight()
        {
            var border = PreparationUi.Fill("Tutorial Highlight", safe);
            var gold = new Color32(255, 218, 124, 255);
            foreach (var edge in new[]
            {
                Box("Bottom", border, 0, 0, 1, 0), Box("Top", border, 0, 1, 1, 1),
                Box("Left", border, 0, 0, 0, 1), Box("Right", border, 1, 0, 1, 1)
            })
            {
                bool horizontal = edge.anchorMin.y == edge.anchorMax.y;
                Vector2 width = horizontal ? Vector2.up : Vector2.right;
                edge.offsetMin = -3 * width;
                edge.offsetMax = 3 * width;
                PreparationUi.Surface(edge, gold).raycastTarget = false;
            }
            var group = border.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            return group;
        }

        private static RectTransform Box(string name, Transform parent, float left, float bottom,
            float right, float top) => PreparationUi.Rect(name, parent, new Vector2(left, bottom),
                new Vector2(right, top), Vector2.zero, Vector2.zero);
    }
}
