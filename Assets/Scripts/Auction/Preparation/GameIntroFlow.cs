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
            EndIncomingCall, EndConversation, EndTransition, EndReport
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
        [SerializeField] private Sprite logo;
        [SerializeField] private Sprite playButton;
        [SerializeField] private Sprite incomingPhone;
        [SerializeField] private Sprite pickedUpPhone;
        [SerializeField] private AudioClip ringtone;
        [SerializeField] private AudioClip pickupClick;
        [SerializeField] private Sprite endFrame;
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
        private MvpAuctionLevel level;
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
        private TMP_Text endMessage;
        private int lastEndTap;
        private bool fadingEndLine;

        internal Sprite ReportLogo => logo;
        internal Sprite ReportButton => playButton;
        internal Sprite EndFrame => endFrame;

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
            lastEndTap = -1;
            fadingEndLine = false;
            Open(Stage.EndIncomingCall);
            DrawEndPhone(false);
            EndTap("Pick Up SALE Call", AnswerEndCall);
            if (ringtone)
            {
                audioSource.clip = ringtone;
                audioSource.loop = true;
                audioSource.Play();
            }
        }

        private void AnswerEndCall()
        {
            if (CurrentStage != Stage.EndIncomingCall)
                return;

            lastEndTap = Time.frameCount;
            audioSource.Stop();
            audioSource.loop = false;
            if (pickupClick)
                audioSource.PlayOneShot(pickupClick);
            Open(Stage.EndConversation);
            DrawEndPhone(true);
            var bubble = Box("SALE Dialogue", safe, .06f, .54f, .94f, .76f);
            var frame = PreparationUi.Surface(bubble, new Color(.78f, .78f, .76f));
            frame.sprite = endFrame;
            if (endFrame)
                frame.type = Image.Type.Sliced;
            var speaker = PreparationUi.Label(Box("SALE Speaker", bubble, .08f, .69f, .92f, .94f),
                "Sa-Le", 34, PreparationUi.Ink, FontStyles.Bold);
            speaker.alignment = TextAlignmentOptions.MidlineLeft;
            endMessage = PreparationUi.Label(Box("SALE Line", bubble, .08f, .07f, .92f, .69f),
                "", 30, PreparationUi.Ink);
            endMessage.enableAutoSizing = true;
            endMessage.fontSizeMin = 23;
            endMessage.fontSizeMax = 32;
            EndTap("Advance SALE Call", AdvanceEndCall);
            ShowNextEndLine();
        }

        private void DrawEndPhone(bool answered)
        {
            Art("SALE Telephone", Box("SALE Telephone", safe, .10f, .10f, .90f, .48f),
                answered ? pickedUpPhone : incomingPhone, "[Telephone]");
            Caption(Box("SALE Call Prompt", safe, .05f, .02f, .95f, .075f),
                answered ? "Tap to continue" : "Tap to pick up", 26);
        }

        private void EndTap(string name, UnityEngine.Events.UnityAction action)
        {
            var hitArea = PreparationUi.Fill(name, view);
            var button = hitArea.gameObject.AddComponent<Button>();
            button.targetGraphic = PreparationUi.Surface(hitArea, Color.clear);
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
        }

        private void AdvanceEndCall()
        {
            if (CurrentStage != Stage.EndConversation || fadingEndLine || lastEndTap == Time.frameCount)
                return;
            lastEndTap = Time.frameCount;
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
                endMessage.text = ending[next++];
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

        internal void ShowStart(PreparationPhase owner, MvpAuctionLevel currentLevel, int order)
        {
            preparation = owner;
            level = currentLevel;
            showEndReport = null;
            StopAllCoroutines();
            if (!canvas)
                Build(order);
            audioSource.Stop();
            fade.gameObject.SetActive(false);
            canvas.gameObject.SetActive(true);
            Open(Stage.StartScreen);
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
            Art("Incoming Telephone", Box("Incoming Telephone", safe, .11f, .08f, .89f, .47f),
                incomingPhone, "[zástupca rotačného telefónu]");
            var pickUp = AddButton("Klepni a zdvihni", Box("Pick Up Phone", safe, .07f, .02f, .93f, .48f), PickUp);
            pickUp.targetGraphic.color = Color.clear;
            pickUp.transition = Selectable.Transition.None;
            if (ringtone)
            {
                audioSource.clip = ringtone;
                audioSource.loop = true;
                audioSource.Play();
            }
        }

        private void PickUp()
        {
            if (CurrentStage != Stage.IncomingCall)
                return;
            audioSource.Stop();
            audioSource.loop = false;
            if (pickupClick)
                audioSource.PlayOneShot(pickupClick);
            Open(Stage.SaleConversation);
            next = 0;
            messages = dialogue.OrderBy(message => message.order).ToArray();
            var advance = AddButton("", PreparationUi.Fill("Advance Conversation", view), Advance);
            advance.targetGraphic.color = Color.clear;
            advance.transform.SetAsFirstSibling();
            view.GetComponent<Image>().raycastTarget = false;

            Art("Picked Up Telephone", Box("Picked Up Telephone", safe, .11f, .08f, .89f, .47f), pickedUpPhone,
                "[zástupca zdvihnutého telefónu]");
            Caption(Box("Continue Prompt", safe, .05f, .02f, .95f, .075f), "Klepni pre pokračovanie", 26);
            var viewport = Box("Conversation", safe, .05f, .48f, .95f, .96f);
            var tap = AddButton("", PreparationUi.Fill("Tap Conversation", viewport), Advance);
            tap.targetGraphic.color = Color.clear;
            tap.transition = Selectable.Transition.None;
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

        }

        private void Advance()
        {
            if (CurrentStage != Stage.SaleConversation)
                return;
            if (next >= messages.Length)
            {
                CurrentStage = Stage.TransitionToNews;
                StartCoroutine(Transition());
                return;
            }

            Message message = messages[next++];
            bool player = message.speaker == Speaker.Player;
            var row = PreparationUi.Fill(player ? "Ja" : "Sa-Le", scroll.content);
            var bubble = Box("Bubble", row, player ? .16f : 0, 0, player ? 1 : .84f, 1);
            var surface = PreparationUi.Surface(bubble, new Color(.83f, .82f, .80f));
            surface.raycastTarget = false;
            var label = PreparationUi.Label(PreparationUi.Rect("Message", bubble, Vector2.zero, Vector2.one,
                new Vector2(18, 12), new Vector2(-18, -12)),
                (player ? "Ja" : "Sa-Le") + "\n" + Format(message.text), 27, PreparationUi.Ink);
            Canvas.ForceUpdateCanvases();
            row.gameObject.AddComponent<LayoutElement>().preferredHeight =
                label.GetPreferredValues(label.text, Mathf.Max(1, scroll.viewport.rect.width * .84f - 36),
                    Mathf.Infinity).y + 24;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 0;
        }

        private string Format(string value) =>
            value.Replace("{budget}", Dollars(level.StartingBudget)).Replace("{profit}", Dollars(level.ProfitTarget));

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
            if (caption == "Klepni a zdvihni")
            {
                label.alignment = TextAlignmentOptions.Bottom;
                label.color = PreparationUi.Ink;
            }
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
