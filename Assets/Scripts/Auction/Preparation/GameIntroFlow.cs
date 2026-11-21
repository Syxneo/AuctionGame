using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    public sealed class GameIntroFlow : MonoBehaviour
    {
        public enum Stage
        {
            StartScreen, IncomingCall, SaleConversation, TransitionToNews, Completed,
            EndIncomingCall, EndConversation, EndTransition, EndReport, CampaignComplete
        }
        private enum Speaker { Sale, Player }

        [Serializable]
        private sealed class Message
        {
            public Speaker speaker;
            public int order;
            [TextArea] public string text;

            public Message(Speaker speaker, int order, string text)
            {
                this.speaker = speaker;
                this.order = order;
                this.text = text;
            }
        }

        [SerializeField] private Sprite parchment;
        [SerializeField] private Texture2D startParchment;
        [SerializeField] private Sprite logo;
        [SerializeField] private Sprite playButton;
        [SerializeField] private Sprite incomingPhone;
        [SerializeField] private Sprite pickedUpPhone;
        [SerializeField] private AudioClip ringtone;
        [SerializeField] private AudioClip pickupClick;
        [SerializeField] private BidderSpeechBubble speechBubble;
        [SerializeField, Range(0, 10)] private float phoneRockDegrees = 6;
        [SerializeField, Min(.1f)] private float phoneRockPeriod = .34f;
        [SerializeField, TextArea] private string[] successEnding =
        {
            "Well, well, well... You actually made a profit.",
            "I must admit, I didn't expect that from you.",
            "Let's see what you've managed to get your hands on."
        };
        [SerializeField, TextArea] private string[] failureEnding =
        {
            "You did terrible. I expected nothing, and somehow you delivered less.",
            "Let's see how much money you've thrown away.",
            "Remember, darling... Everything is for sale."
        };
        [SerializeField, Min(0)] private float fadeOut = .25f;
        [SerializeField, Min(0)] private float fadeIn = .25f;
        [SerializeField, Min(0)] private float endLineFade = .10f;
        [SerializeField] private Message[] dialogue =
        {
            new(Speaker.Sale, 0, "Ahoj, videl si už dnešné NOVINKY? Údajne tam budú super veci na nákup..."),
            new(Speaker.Player, 1, "Fakt? Aký máme dnes rozpočet?"),
            new(Speaker.Sale, 2, "Máme {budget}. Dosiahni aspoň {profit} zisku. Nesklam ma.")
        };

        public Stage CurrentStage { get; private set; } = Stage.Completed;

        private PreparationPhase preparation;

        private AudioSource audioSource;
        private Canvas canvas;
        private RectTransform view;
        private RectTransform safe;
        private Image fade;
        private ScrollRect scroll;
        private Message[] messages;
        private int next;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;
        private string[] ending;
        private Action<RectTransform> showEndReport;
        private BidderSpeechBubble endBubble;
        private TMP_Text endMessage;
        private int lastCallTap;
        private bool fadingEndLine;

        internal Sprite ReportLogo => logo;
        internal Sprite ReportButton => playButton;
        internal BidderSpeechBubble SpeechBubble => speechBubble;

        internal void ShowEndCall(bool succeeded, Action<RectTransform> openReport)
        {
            if (!canvas)
                Build(101);
            StopAllCoroutines();
            audioSource.Stop();
            fade.gameObject.SetActive(false);
            canvas.gameObject.SetActive(true);
            ending = succeeded ? successEnding : failureEnding;
            showEndReport = openReport;
            next = 0;
            lastCallTap = -1;
            fadingEndLine = false;
            Open(Stage.EndIncomingCall);
            DrawPhone("SALE Telephone", false, "Tap to pick up");
            CallTap("Pick Up SALE Call", AnswerEndCall);
            StartRinging();
        }

        private void AnswerEndCall()
        {
            if (CurrentStage != Stage.EndIncomingCall)
                return;

            StopRinging();
            Open(Stage.EndConversation);
            DrawPhone("SALE Telephone", true, "Tap to continue");
            endBubble = BidderSpeechBubble.CreateDialogue(speechBubble,
                Box("SALE Dialogue", safe, .06f, .51f, .94f, .82f), "SALE Line", 36);
            CallTap("Advance SALE Call", AdvanceEndCall);
            ShowNextEndLine();
        }

        private void DrawPhone(string name, bool answered, string prompt)
        {
            var slot = Box("Telephone Position", safe, .11f, .10f, .89f, .44f);
            var artwork = PreparationUi.Fill(name, slot);
            Art(name, artwork, answered && pickedUpPhone ? pickedUpPhone : incomingPhone, "[Telefón]");
            artwork.gameObject.AddComponent<TelephonePresentation>().Present(answered, phoneRockDegrees, phoneRockPeriod);
            Caption(Box("SALE Call Prompt", safe, .05f, .02f, .95f, .075f), prompt, 26);
        }

        private void StartRinging()
        {
            if (!ringtone)
                return;
            audioSource.clip = ringtone;
            audioSource.loop = true;
            audioSource.Play();
        }

        private void StopRinging()
        {
            lastCallTap = Time.frameCount;
            audioSource.Stop();
            audioSource.loop = false;
            if (pickupClick)
                audioSource.PlayOneShot(pickupClick);
        }

        private void CallTap(string name, UnityEngine.Events.UnityAction action)
        {
            var hitArea = PreparationUi.Fill(name, view);
            var button = hitArea.gameObject.AddComponent<Button>();
            button.targetGraphic = PreparationUi.Surface(hitArea, Color.clear);
            button.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
        }

        private void AdvanceEndCall()
        {
            if (CurrentStage != Stage.EndConversation || fadingEndLine || lastCallTap == Time.frameCount)
                return;
            lastCallTap = Time.frameCount;
            if (ending != null && next < ending.Length && endLineFade > 0)
            {
                fadingEndLine = true;
                StartCoroutine(FadeToNextEndLine());
                return;
            }
            ShowNextEndLine();
        }

        private IEnumerator FadeToNextEndLine()
        {
            endMessage.CrossFadeAlpha(0, endLineFade, true);
            yield return new WaitForSecondsRealtime(endLineFade);
            ShowNextEndLine();
            endMessage.CrossFadeAlpha(1, endLineFade, true);
            yield return new WaitForSecondsRealtime(endLineFade);
            fadingEndLine = false;
        }

        private void ShowNextEndLine()
        {
            if (ending != null && next < ending.Length)
            {
                endMessage = endBubble.SetDialogue(ending[next++]);
                return;
            }
            CurrentStage = Stage.EndTransition;
            StartCoroutine(TransitionToReport());
        }

        private IEnumerator TransitionToReport()
        {
            fade.gameObject.SetActive(true);
            yield return Fade(0, 1, fadeOut);
            Open(Stage.EndReport);
            var openReport = showEndReport;
            showEndReport = null;
            openReport?.Invoke(safe);
            yield return Fade(1, 0, fadeIn);
            fade.gameObject.SetActive(false);
        }

        internal void ShowStart(PreparationPhase owner, int order)
        {
            preparation = owner;
            showEndReport = null;
            StopAllCoroutines();
            if (!canvas)
                Build(order);
            audioSource.Stop();
            fade.gameObject.SetActive(false);
            canvas.gameObject.SetActive(true);
            Open(Stage.StartScreen);
            if (owner.CampaignDayCount > 0)
                Caption(Box("Campaign Day", safe, .08f, .105f, .92f, .185f),
                    $"DAY {owner.CurrentDay} / {owner.CampaignDayCount}", 56);
            Art("Auction Frenzy Logo", Box("Logo", safe, .07f, .40f, .93f, .84f), logo,
                "DRAŽOBNÁ HORÚČKA\n[zástupca loga]");
            var play = AddButton("HRAŤ!", Box("PLAY", safe, .06f, .19f, .94f, .41f), Play);
            if (playButton)
            {
                var image = (Image)play.targetGraphic;
                image.sprite = playButton;
                image.color = Color.white;
                image.preserveAspect = true;
                var label = play.GetComponentInChildren<TMP_Text>();
                label.color = Color.black;
                label.fontStyle = FontStyles.Bold;
                label.fontSize = 56;
            }
        }

        internal void ShowCampaignComplete(PreparationPhase owner, int order)
        {
            StopAllCoroutines();
            if (!canvas)
                Build(order);
            audioSource.Stop();
            fade.gameObject.SetActive(false);
            canvas.gameObject.SetActive(true);
            Open(Stage.CampaignComplete);
            var gold = new Color32(241, 195, 104, 255);
            var cream = new Color32(255, 240, 213, 255);
            view.GetComponent<Image>().color = new Color32(62, 23, 32, 255);
            var frame = Box("Celebration Frame", safe, .07f, .30f, .93f, .87f);
            PreparationUi.Surface(frame, gold).raycastTarget = false;
            var card = PreparationUi.Rect("Celebration Card", frame, Vector2.zero, Vector2.one,
                new Vector2(3, 3), new Vector2(-3, -3));
            PreparationUi.Surface(card, new Color32(79, 26, 38, 255)).raycastTarget = false;
            var confetti = PreparationUi.Fill("Celebration Confetti", safe);
            var headline = Box("Campaign Complete", safe, .10f, .45f, .90f, .68f);
            var title = PreparationUi.Label(headline, "CAMPAIGN\nCOMPLETE", 68, gold, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Center;
            title.enableAutoSizing = true;
            title.fontSizeMin = 34;
            title.fontSizeMax = 68;

            if (logo)
                Art("Celebration Logo", Box("Celebration Logo Space", safe, .28f, .69f, .72f, .84f), logo, "");
            var message = PreparationUi.Label(Box("Congratulations", safe, .11f, .33f, .89f, .44f),
                $"Gratulujeme! Zvládol si všetkých {owner.CampaignDayCount} aukčných dní.", 29, cream);
            message.alignment = TextAlignmentOptions.Center;

            var status = PreparationUi.Label(Box("Restart Error", safe, .10f, .09f, .90f, .16f),
                "", 24, cream);
            status.alignment = TextAlignmentOptions.Center;
            var restart = AddButton("Nová kampaň", Box("Restart Campaign", safe, .15f, .17f, .85f, .27f), () =>
            {
                if (CurrentStage == Stage.CampaignComplete && !owner.TryRestartCampaign(out string error))
                    status.text = error ?? "";
            });
            restart.targetGraphic.color = gold;
            restart.GetComponentInChildren<TMP_Text>().color = PreparationUi.Ink;
            StartCoroutine(Celebrate(confetti, headline, gold, cream));
        }

        private IEnumerator Celebrate(RectTransform layer, RectTransform headline, Color gold, Color cream)
        {
            var random = new System.Random(421);
            var pieces = new (Image image, Vector2 start, Vector2 end, float spin, float delay)[32];
            var colors = new Color[] { gold, cream, new Color32(219, 78, 105, 255) };
            for (int index = 0; index < pieces.Length; index++)
            {
                bool left = index % 2 == 0;
                float side = left ? -1f : 1f;
                var start = new Vector2(left ? .14f : .86f, .91f);
                var end = new Vector2(.5f + side * Mathf.Lerp(.35f, -.38f, (float)random.NextDouble()),
                    Mathf.Lerp(.18f, .63f, (float)random.NextDouble()));
                var rect = Box("Confetti", layer, start.x, start.y, start.x, start.y);
                rect.sizeDelta = new Vector2(random.Next(6, 14), random.Next(9, 20));
                var image = PreparationUi.Surface(rect, colors[index % colors.Length]);
                image.raycastTarget = false;
                pieces[index] = (image, start, end, side * random.Next(120, 500),
                    (float)random.NextDouble() * .4f);
            }

            const float flightTime = 1.45f;
            const float duration = flightTime + .4f;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                headline.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0, 1, elapsed / .32f));
                foreach (var piece in pieces)
                {
                    float progress = Mathf.Clamp01((elapsed - piece.delay) / flightTime);
                    var rect = piece.image.rectTransform;
                    var anchor = Vector2.Lerp(piece.start, piece.end, progress);
                    anchor.y += Mathf.Sin(progress * Mathf.PI) * .10f;
                    rect.anchorMin = rect.anchorMax = anchor;
                    rect.localRotation = Quaternion.Euler(0, 0, piece.spin * progress);
                    var color = piece.image.color;
                    color.a = Mathf.InverseLerp(0, .07f, elapsed - piece.delay) *
                        (1f - Mathf.InverseLerp(.64f, 1f, progress));
                    piece.image.color = color;
                }
                yield return null;
            }

            headline.localScale = Vector3.one;
            Destroy(layer.gameObject);
        }

        private void Build(int order)
        {
            var root = new GameObject("Game Intro Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            fade = PreparationUi.Surface(PreparationUi.Fill("Intro Fade", root.transform), Color.black);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        private void Open(Stage stage)
        {
            CurrentStage = stage;
            if (view)
            {
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }

            view = PreparationUi.Fill(stage.ToString(), canvas.transform);
            fade.transform.SetAsLastSibling();
            bool endingStage = stage is Stage.EndIncomingCall or Stage.EndConversation or Stage.EndReport;
            Color fallback = endingStage ? new Color(.89f, .84f, .53f) : PreparationUi.Paper;
            if (stage == Stage.StartScreen && startParchment)
            {
                var background = view.gameObject.AddComponent<RawImage>();
                background.texture = startParchment;
                background.raycastTarget = false;
            }
            else
                PreparationUi.Surface(view, parchment ? Color.white : fallback).sprite = parchment;
            safe = PreparationUi.Fill("Intro Safe Area", view);
            lastScreen = default;
            Update();
        }

        private void Update()
        {
            if (!safe || Screen.width <= 0 || Screen.height <= 0)
                return;
            Rect area = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (area == lastSafeArea && screen == lastScreen)
                return;
            lastSafeArea = area;
            lastScreen = screen;
            safe.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            safe.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
        }

        private void Play()
        {
            if (CurrentStage != Stage.StartScreen)
                return;
            Open(Stage.IncomingCall);
            DrawPhone("Incoming Telephone", false, "Klepni a zdvihni");
            CallTap("Pick Up Phone", PickUp);
            StartRinging();
        }

        private void PickUp()
        {
            if (CurrentStage != Stage.IncomingCall)
                return;
            StopRinging();
            Open(Stage.SaleConversation);
            next = 0;
            var custom = preparation.SaleDialogue?.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            messages = custom is { Length: > 0 }
                ? custom.Select((line, index) => new Message(Speaker.Sale, index, line)).ToArray()
                : dialogue.OrderBy(message => message.order).ToArray();
            if (preparation.CampaignDayCount > 0 &&
                !messages.Any(message => message.speaker == Speaker.Sale &&
                    message.text.Contains("{budget}") && message.text.Contains("{profit}")))
                messages = messages.Append(new Message(Speaker.Sale, messages.Length,
                    "Máš {budget}. Dosiahni aspoň {profit} zisku.")).ToArray();
            CallTap("Advance Conversation", Advance);
            view.Find("Advance Conversation").SetAsFirstSibling();
            view.GetComponent<Image>().raycastTarget = false;
            DrawPhone("Picked Up Telephone", true, "Klepni pre pokračovanie");
            var viewport = Box("Conversation", safe, .05f, .48f, .95f, .96f);
            var tap = PreparationUi.Button(PreparationUi.Fill("Tap Conversation", viewport), "", false);
            tap.targetGraphic.color = Color.clear;
            tap.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(Advance);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = PreparationUi.Rect("Messages", viewport, Vector2.up, Vector2.one,
                Vector2.zero, Vector2.zero);
            content.pivot = Vector2.up;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            ShowNextMessage();
        }

        private void Advance()
        {
            if (CurrentStage != Stage.SaleConversation || lastCallTap == Time.frameCount)
                return;
            lastCallTap = Time.frameCount;
            ShowNextMessage();
        }

        private void ShowNextMessage()
        {
            if (next >= messages.Length)
            {
                CurrentStage = Stage.TransitionToNews;
                StartCoroutine(Transition());
                return;
            }

            Message message = messages[next++];
            bool player = message.speaker == Speaker.Player;
            var row = PreparationUi.Fill(player ? "Ja" : "Sa-Le", scroll.content);
            var region = Box("Bubble Region", row, player ? .10f : 0, 0, player ? 1 : .90f, 1);
            var rowLayout = row.gameObject.AddComponent<LayoutElement>();
            Canvas.ForceUpdateCanvases();
            var bubble = BidderSpeechBubble.CreateDialogue(speechBubble, region, "Message", 36, rowLayout);
            bubble.SetDialogue((player ? "Ja" : "Sa-Le") + "\n" + Format(message.text));
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 0;
        }

        private string Format(string value) =>
            value.Replace("{budget}", Dollars(preparation.StartingBudget)).Replace("{profit}", Dollars(preparation.ProfitTarget));

        private static string Dollars(int value) =>
            "$" + value.ToString("N0", CultureInfo.GetCultureInfo("en-US"));

        private IEnumerator Transition()
        {
            fade.gameObject.SetActive(true);
            yield return Fade(0, 1, fadeOut);
            view.gameObject.SetActive(false);
            preparation.EnterNewsFromIntro();
            var events = EventSystem.current;
            var selected = events ? events.currentSelectedGameObject : null;
            if (events)
                events.SetSelectedGameObject(null);
            yield return Fade(1, 0, fadeIn);
            CurrentStage = Stage.Completed;
            if (events && selected && selected.activeInHierarchy)
                events.SetSelectedGameObject(selected);
            canvas.gameObject.SetActive(false);
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            fade.color = new Color(0, 0, 0, from);
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }
            fade.color = new Color(0, 0, 0, to);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (audioSource)
                audioSource.Stop();
            if (canvas)
                canvas.gameObject.SetActive(false);
        }

        private static RectTransform Box(string name, Transform parent, float left, float bottom,
            float right, float top) => PreparationUi.Rect(name, parent, new Vector2(left, bottom),
                new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Button AddButton(string caption, RectTransform rect, UnityEngine.Events.UnityAction action)
        {
            var button = PreparationUi.Button(rect, caption);
            var label = rect.GetComponentInChildren<TMP_Text>();
            label.fontSize = 38;
            button.onClick.AddListener(action);
            return button;
        }

        private static void Art(string name, RectTransform rect, Sprite sprite, string placeholder)
        {
            if (sprite)
            {
                var image = PreparationUi.Surface(rect, Color.white);
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
                Caption(PreparationUi.Fill(name + " Placeholder", rect), placeholder, 32);
        }

        private static void Caption(RectTransform rect, string value, int size) =>
            PreparationUi.Label(rect, value, size, PreparationUi.Ink, FontStyles.Bold).alignment =
                TextAlignmentOptions.Center;
    }
}
