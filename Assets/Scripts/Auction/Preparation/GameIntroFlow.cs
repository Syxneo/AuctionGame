using System;
using System.Collections;
using System.Collections.Generic;
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
            EndIncomingCall, EndConversation, EndTransition, EndReport, CampaignComplete,
            FinalReveal, FinalAuctionCard, FinalStatement
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
        [Header("Start Screen Effects")]
        [SerializeField] private bool enableAmbientDust = true;
        [SerializeField, Range(4, 8)] private int dustDensity = 6;
        [SerializeField, Range(0, .35f)] private float dustOpacity = .16f;
        [SerializeField] private bool enablePlayPress = true;
        [SerializeField, Range(.8f, 1f)] private float playPressScale = .94f;
        [SerializeField, Min(.01f)] private float playReleaseDuration = .18f;
        [Header("Telephone Effects")]
        [SerializeField] private bool enableRingLines = true;
        [SerializeField, Range(0, 1)] private float ringLineOpacity = .65f;
        [SerializeField, Range(.5f, 1.5f)] private float ringLineScale = 1f;
        [SerializeField, TextArea] private string[] successEnding =
        {
            "Ale, ale, ale... Ty si naozaj dosiahol zisk.",
            "Priznávam, to som od teba nečakal.",
            "Pozrime sa, čo sa ti podarilo uchmatnúť."
        };
        [SerializeField, TextArea] private string[] failureEnding =
        {
            "To bola bieda. Nečakal som nič, a ty si zvládol ešte menej.",
            "Pozrime sa, koľko peňazí si vyhodil von oknom.",
            "Pamätaj, zlatko... Všetko je na predaj."
        };
        [SerializeField, TextArea] private string[] finalReveal =
        {
            "Už ste si prezreli vyúčtovanie? Výborne. Teraz sa môžeme porozprávať o vašej skutočnej hodnote.",
            "Všimli ste si niekedy, aký dobrý výhľad je z toho aukčného pódia?",
            "Päť dní som odtiaľ sledoval, ako prihadzujete, riskujete a snažíte sa vyhrať.",
            "Áno. Ten usmiaty aukcionár som bol celý čas ja.",
            "A vy ste si mysleli, že ste tam prišli iba nakupovať. Rozkošné!",
            "Viete, predmety, ktoré ste mi priniesli, boli len začiatok. Najzaujímavejšiu investíciu som mal pred očami každý deň.",
            "Vás."
        };
        [SerializeField, TextArea] private string[] finalStatement =
        {
            "Nemajte obavy. Váš nový majiteľ bude nadšený.",
            "Ďakujem za spoluprácu.",
            "A pamätajte... všetko je na predaj."
        };
        [SerializeField, Min(0)] private float fadeOut = .25f;
        [SerializeField, Min(0)] private float fadeIn = .25f;
        [SerializeField] private Message[] dialogue =
        {
            new(Speaker.Sale, 0, "Ahoj, videl si už dnešné NEWSSSS? Údajne tam budú super veci na nákup..."),
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
        private (RectTransform row, BidderSpeechBubble bubble, bool player)[] callRows;
        private RectTransform saleHeader;
        private BidderSpeechBubble headerBubble;
        private Texture2D silhouette;
        private const float SaleHeaderHeight = 36;
        private int next;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;
        private string[] ending;
        private Action<RectTransform> showEndReport;
        private int lastCallTap;
        private Image[] dust;
        private float dustStarted;

        internal bool IsFading => fade && fade.gameObject.activeSelf;
        internal Sprite ReportLogo => logo;
        internal Sprite ReportButton => playButton;
        internal BidderSpeechBubble SpeechBubble => speechBubble;

        internal void ShowEndCall(bool succeeded, Action<RectTransform> openReport, string[] dayEnding = null)
        {
            if (!canvas)
                Build(101);
            StopAllCoroutines();
            audioSource.Stop();
            fade.gameObject.SetActive(false);
            canvas.gameObject.SetActive(true);
            ending = dayEnding is { Length: > 0 } ? dayEnding : succeeded ? successEnding : failureEnding;
            showEndReport = openReport;
            next = 0;
            lastCallTap = -1;
            Open(Stage.EndIncomingCall);
            DrawPhone("SALE Telephone", false, "Klepni a zdvihni");
            CallTap("Pick Up SALE Call", AnswerEndCall);
            StartRinging();
        }

        private void AnswerEndCall()
        {
            if (CurrentStage != Stage.EndIncomingCall)
                return;

            StopRinging();
            Open(Stage.EndConversation);
            DrawPhone("SALE Telephone", true, "Klepni a pokračuj");
            BuildConversation("SALE Dialogue", "Advance SALE Call", AdvanceEndCall, alignBottom: true);
            PrepareCallRows((ending ?? Array.Empty<string>()).Select(text => (text, false)), "SALE Line");
            ShowNextEndLine();
        }

        private void DrawPhone(string name, bool answered, string prompt)
        {
            var slot = Box("Telephone Position", safe, .11f, .10f, .89f, .44f);
            var artwork = PreparationUi.Fill(name, slot);
            Art(name, artwork, answered && pickedUpPhone ? pickedUpPhone : incomingPhone, "[Telefón]");
            artwork.gameObject.AddComponent<TelephonePresentation>().Present(answered, phoneRockDegrees,
                phoneRockPeriod, enableRingLines ? ringLineOpacity : 0, ringLineScale);
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
            if (CurrentStage != Stage.EndConversation || lastCallTap == Time.frameCount)
                return;
            lastCallTap = Time.frameCount;
            ShowNextEndLine();
        }

        private void ShowNextEndLine()
        {
            if (ending != null && next < ending.Length)
            {
                ShowCallRow(next++);
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

        internal void ShowFinale(Sprite cardParchment, Action complete)
        {
            StopAllCoroutines();
            fade.gameObject.SetActive(false);
            ShowFinalDialogue(Stage.FinalReveal, finalReveal,
                () => StartCoroutine(ShowFinalAuctionCard(cardParchment, complete)));
        }

        private void ShowFinalDialogue(Stage stage, string[] lines, Action finished)
        {
            Open(stage);
            DrawPhone("SALE Telephone", true, "Klepni a pokračuj");
            int line = 0;
            lastCallTap = Time.frameCount;
            void AdvanceLine()
            {
                if (CurrentStage != stage || lastCallTap == Time.frameCount)
                    return;

                lastCallTap = Time.frameCount;
                if (++line == lines.Length)
                {
                    finished();
                    return;
                }

                callRows[line - 1].row.gameObject.SetActive(false);
                ShowCallRow(line);
            }

            BuildConversation("SALE Dialogue", "Advance Finale", AdvanceLine, alignBottom: true);
            PrepareCallRows(lines.Select(text => (text, false)), "SALE Line");
            ShowCallRow(line);
        }

        private IEnumerator ShowFinalAuctionCard(Sprite cardParchment, Action complete)
        {
            Open(Stage.FinalAuctionCard);
            var background = view.GetComponent<Image>();
            if (cardParchment)
            {
                background.sprite = cardParchment;
                background.color = Color.white;
            }

            var frame = Box("Final Auction Card", safe, .07f, .18f, .93f, .82f);
            PreparationUi.Surface(frame, PreparationUi.Ink);
            var paper = PreparationUi.Rect("Final Auction Paper", frame, Vector2.zero, Vector2.one,
                new Vector2(5, 5), new Vector2(-5, -5));
            PreparationUi.Surface(paper, PreparationUi.Paper);
            Caption(Box("Final Lot Header", paper, .08f, .87f, .92f, .97f), "POSLEDNÝ PREDMET", 40);
            var itemFrame = Box("Final Lot Frame", paper, .24f, .43f, .76f, .85f);
            PreparationUi.Surface(itemFrame, PreparationUi.Ink);
            var mount = PreparationUi.Rect("Final Lot Mount", itemFrame, Vector2.zero, Vector2.one,
                new Vector2(3, 3), new Vector2(-3, -3));
            PreparationUi.Surface(mount, new Color32(233, 216, 181, 255));

            yield return new WaitForSecondsRealtime(.55f);
            var portraitSpace = Box("Final Lot Art", mount, .12f, .26f, .88f, .96f);
            var portrait = AddAnonymousSilhouette(PreparationUi.Fill("Final Lot Silhouette", portraitSpace));
            portrait.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            Caption(Box("Final Lot Name", mount, .10f, .015f, .90f, .28f), "TY", 64);
            Caption(Box("Final Lot Description", paper, .09f, .28f, .91f, .42f),
                "Päť dní testovaný. Ochotný riskovať. Výborný stav.", 34);
            Caption(Box("Final Lot Buyer", paper, .08f, .19f, .92f, .28f), "NEZNÁMY KUPUJÚCI", 36);

            yield return new WaitForSecondsRealtime(.55f);
            var stamp = Box("Final Lot Result", paper, .19f, .045f, .81f, .17f);
            stamp.localRotation = Quaternion.Euler(0, 0, -5);
            PreparationUi.Surface(stamp, new Color32(133, 37, 40, 255));
            PreparationUi.Label(PreparationUi.Fill("Sold Caption", stamp), "PREDANÉ", 56,
                PreparationUi.Paper, FontStyles.Bold).alignment =
                TextAlignmentOptions.Center;

            AddButton("POKRAČOVAŤ", Box("Continue Final Card", safe, .19f, .07f, .81f, .145f), () =>
            {
                if (CurrentStage == Stage.FinalAuctionCard)
                    ShowFinalDialogue(Stage.FinalStatement, finalStatement, complete);
            });
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
            if (enableAmbientDust && dustOpacity > 0)
                BuildDust();
            if (owner.CampaignDayCount > 0)
                Caption(Box("Campaign Day", safe, .08f, .105f, .92f, .185f),
                    $"DEŇ {owner.CurrentDay} / {owner.CampaignDayCount}", 56);
            Art("Auction Frenzy Logo", Box("Logo", safe, .07f, .40f, .93f, .84f), logo,
                "AUCTION FRENZY");
            var play = AddButton("HRAŤ", Box("PLAY", safe, .06f, .19f, .94f, .41f), Play, animatePress: false);
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
            if (enablePlayPress)
                BidButtonPress.Bind(play, playPressScale, 1.04f, playReleaseDuration);
        }

        private void BuildDust()
        {
            var layer = PreparationUi.Fill("Paper Dust", safe);
            dust = new Image[dustDensity];
            dustStarted = Time.unscaledTime;
            for (int i = 0; i < dust.Length; i++)
            {
                var speck = PreparationUi.Fill("Dust Speck", layer);
                speck.sizeDelta = new Vector2(3 + i % 3, 2 + i % 2);
                speck.localRotation = Quaternion.Euler(0, 0, 17f * i);
                dust[i] = PreparationUi.Surface(speck, Color.clear);
                dust[i].raycastTarget = false;
            }
            UpdateDust();
        }

        private void UpdateDust()
        {
            float elapsed = Time.unscaledTime - dustStarted;
            for (int i = 0; i < dust.Length; i++)
            {
                float progress = Mathf.Repeat(elapsed / (8f + i % 3) + (i + .5f) / dust.Length, 1f);
                // Stay outside the PLAY rectangle, including at its release overshoot.
                float x = i % 2 == 0 ? .021f : .979f;
                float y = .10f + .24f * (i / 2);
                var anchor = new Vector2(x + .008f * Mathf.Sin(progress * 2 * Mathf.PI), y + .13f * progress);
                var speck = dust[i];
                speck.rectTransform.anchorMin = speck.rectTransform.anchorMax = anchor;
                speck.color = new Color(1f, i % 2 == 0 ? .94f : .82f, .57f,
                    dustOpacity * Mathf.Sin(progress * Mathf.PI));
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
            var title = PreparationUi.Label(headline, "EVERYTHING IS FOR SALE", 68, gold, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Center;
            title.enableAutoSizing = true;
            title.fontSizeMin = 34;
            title.fontSizeMax = 68;

            if (logo)
                Art("Celebration Logo", Box("Celebration Logo Space", safe, .28f, .69f, .72f, .84f), logo, "");
            var message = PreparationUi.Label(Box("Congratulations", safe, .11f, .33f, .89f, .44f),
                "KONIEC", 35, cream);
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
            dust = null;
            if (saleHeader)
            {
                saleHeader.gameObject.SetActive(false);
                saleHeader.SetParent(canvas.transform, false);
                headerBubble = null;
            }
            callRows = null;
            if (view)
            {
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }

            view = PreparationUi.Fill(stage.ToString(), canvas.transform);
            fade.transform.SetAsLastSibling();
            bool endingStage = stage is Stage.EndIncomingCall or Stage.EndConversation or Stage.EndReport
                or Stage.FinalReveal or Stage.FinalStatement;
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
            if (dust != null && view.gameObject.activeInHierarchy)
                UpdateDust();
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
            var messages = custom is { Length: > 0 }
                ? custom.Select((line, index) => new Message(Speaker.Sale, index, line)).ToArray()
                : dialogue.OrderBy(message => message.order).ToArray();
            if (preparation.CampaignDayCount > 0 &&
                !messages.Any(message => message.speaker == Speaker.Sale &&
                    (message.text.Contains("{budget}") && message.text.Contains("{profit}") ||
                     message.text.Contains("{budgetAmount}") && message.text.Contains("{profitAmount}"))))
                messages = messages.Append(new Message(Speaker.Sale, messages.Length,
                    "Máš {budget}. Dosiahni aspoň {profit} zisku.")).ToArray();
            DrawPhone("Picked Up Telephone", true, "Klepni a pokračuj");
            BuildConversation("Conversation", "Advance Conversation", Advance);
            PrepareCallRows(messages.Select(message => (Format(message.text), message.speaker == Speaker.Player)),
                "Message");
            ShowNextMessage();
        }

        private void BuildConversation(string name, string advanceName, UnityEngine.Events.UnityAction advance,
            bool alignBottom = false)
        {
            CallTap(advanceName, advance);
            view.Find(advanceName).SetAsFirstSibling();
            view.GetComponent<Image>().raycastTarget = false;
            var viewport = Box(name, safe, .05f, .48f, .95f, .96f);
            var tap = PreparationUi.Button(PreparationUi.Fill("Tap Conversation", viewport), "", false);
            tap.targetGraphic.color = Color.clear;
            tap.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(advance);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // Pin the end-call history to the bottom, even while all messages fit.
            float edge = alignBottom ? 0 : 1;
            var content = PreparationUi.Rect("Messages", viewport, new Vector2(0, edge), new Vector2(1, edge),
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0, edge);
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
            if (CurrentStage != Stage.SaleConversation || lastCallTap == Time.frameCount)
                return;
            lastCallTap = Time.frameCount;
            ShowNextMessage();
        }

        private void ShowNextMessage()
        {
            if (next >= callRows.Length)
            {
                CurrentStage = Stage.TransitionToNews;
                StartCoroutine(Transition());
                return;
            }

            ShowCallRow(next++);
        }

        private void PrepareCallRows(IEnumerable<(string text, bool player)> lines,
            string labelName)
        {
            var pool = PreparationUi.Fill("Pending Call Lines", scroll.viewport);
            pool.gameObject.SetActive(false);
            callRows = lines.Select(line =>
            {
                bool player = line.player;
                var row = PreparationUi.Fill(player ? "Ja" : "SALE", pool);
                var region = Box("Bubble Region", row, player ? .10f : 0, 0, player ? 1 : .90f, 1);
                var rowLayout = row.gameObject.AddComponent<LayoutElement>();
                var bubble = BidderSpeechBubble.CreateDialogue(speechBubble, region, labelName, 36, rowLayout);
                bubble.Label.text = player ? "Ja\n" + line.text : line.text;
                return (row, bubble, player);
            }).ToArray();
            if (!saleHeader && callRows.Any(line => !line.player))
                BuildSaleHeader();
        }

        private void ShowCallRow(int index)
        {
            var (row, bubble, player) = callRows[index];
            row.SetParent(scroll.content, false);
            row.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (!player)
            {
                if (headerBubble)
                    headerBubble.SetDialogueHeaderHeight(0);
                saleHeader.SetParent(bubble.Rect, false);
                saleHeader.gameObject.SetActive(true);
                headerBubble = bubble;
                bubble.SetDialogueHeaderHeight(SaleHeaderHeight);
            }
            bubble.SetDialogue(bubble.Label.text);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 0;
            if (index > 0)
                ButtonClickAudio.Find(gameObject.scene)?.PlayClick();
        }

        private void BuildSaleHeader()
        {
            var topLeft = Vector2.up;
            saleHeader = PreparationUi.Rect("SALE Speaker Header", canvas.transform, topLeft, Vector2.one,
                new Vector2(24, -60), new Vector2(-24, -24));
            AddAnonymousSilhouette(PreparationUi.Rect("Anonymous Silhouette", saleHeader, topLeft, topLeft,
                new Vector2(0, -28), new Vector2(27, -1)));

            var name = PreparationUi.Rect("Speaker Name", saleHeader, Vector2.zero, Vector2.one,
                new Vector2(36, 5), Vector2.zero);
            PreparationUi.Label(name, "SALE", 26, PreparationUi.Ink, FontStyles.Bold).alignment =
                TextAlignmentOptions.MidlineLeft;
            var rule = PreparationUi.Rect("Header Rule", saleHeader, Vector2.zero, Vector2.right,
                Vector2.zero, new Vector2(0, 1));
            PreparationUi.Surface(rule, new Color(.24f, .15f, .09f, .42f)).raycastTarget = false;
            saleHeader.gameObject.SetActive(false);
        }

        private RawImage AddAnonymousSilhouette(RectTransform rect)
        {
            if (!silhouette)
                BuildSilhouette();
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = silhouette;
            image.raycastTarget = false;
            return image;
        }

        private void BuildSilhouette()
        {
            silhouette = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            var pixels = new Color32[48 * 48];
            for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
            {
                Vector2 point = new Vector2(x + .5f, y + .5f);
                float head = 10f - Vector2.Distance(point, new Vector2(24, 32));
                float shoulders = (1f - Vector2.Scale(point - new Vector2(24, 7),
                    new Vector2(1f / 19, 1f / 14)).magnitude) * 14;
                float neck = Mathf.Min(5 - Mathf.Abs(point.x - 24), 4 - Mathf.Abs(point.y - 22));
                float shape = Mathf.Max(head, Mathf.Max(neck, shoulders));
                float alpha = Mathf.Clamp01(Mathf.Min(shape + .5f, point.y - 5));
                pixels[y * 48 + x] = new Color32(0, 0, 0, (byte)(255 * alpha));
            }
            silhouette.SetPixels32(pixels);
            silhouette.Apply(false, true);
        }

        private string Format(string value) =>
            value.Replace("{budget}", Dollars(preparation.StartingBudget))
                .Replace("{profit}", Dollars(preparation.ProfitTarget))
                .Replace("{budgetAmount}", preparation.StartingBudget.ToString(CultureInfo.InvariantCulture))
                .Replace("{profitAmount}", preparation.ProfitTarget.ToString(CultureInfo.InvariantCulture));

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

        private void OnDestroy()
        {
            if (silhouette)
                Destroy(silhouette);
        }

        private static RectTransform Box(string name, Transform parent, float left, float bottom,
            float right, float top) => PreparationUi.Rect(name, parent, new Vector2(left, bottom),
                new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Button AddButton(string caption, RectTransform rect, UnityEngine.Events.UnityAction action,
            bool animatePress = true)
        {
            var button = PreparationUi.Button(rect, caption, animatePress: animatePress);
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
