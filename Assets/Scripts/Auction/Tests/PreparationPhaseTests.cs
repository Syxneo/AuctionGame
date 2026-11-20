using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class PreparationPhaseTests
    {
        [Test]
        public void ApprovedCluesKeepTheirKeysSectionsAndCatalogMappings()
        {
            var source = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            Assert.That(source, Is.Not.Null);
            var expected = new[]
            {
                ("art_venus",
                    "Venuša bez rúk sa vynorila zo súkromnej zbierky.",
                    "Antická socha z bieleho mramoru nemá ruky a hlavu má sklonenú doľava. Odborníci hovoria o objave storočia."),
                ("arch_chalice",
                    "Zlatý kalich bez uší.",
                    "Archeológovia vykopali jednoduchý zlatý kalich s tromi červenými kameňmi. Nemá žiadne uchá, presne ako na starých kresbách."),
                ("arch_sword",
                    "Rímsky meč s rovnou čepeľou.",
                    "Pri Dunaji sa našiel krátky meč s rovnou čepeľou a ostrou špičkou. Legionári takými bojovali v prvej línii."),
                ("art_vase",
                    "Modrá amfora z potopenej lode.",
                    "Potápači vyniesli na breh tmavomodrú vázu s bielymi vlnami okolo pásu. Je jediná svojho druhu."),
                ("pop_cd",
                    "Prvé vydanie bez škrabanca.",
                    "Na aukciu ide CD z prvého vydania albumu. Disk je celý, bez jedinej praskliny."),
                ("silly_tears",
                    "Slzy nepriateľov pod korkom.",
                    "Zberateľ ich vraj plnil tridsať rokov. Pravá fľaštička je uzavretá obyčajnou korkovou zátkou.")
            };
            CollectionAssert.AreEqual(expected, source.Clues.Select(clue =>
                (clue.ItemType.TypeId, clue.Headline, clue.Body)));
            CollectionAssert.AreEqual(expected.Select(article => $"news_{article.Item1}"),
                source.Clues.Select(clue => clue.ClueKey));
            Assert.That(source.Clues.Count(clue => clue.Importance == NewspaperImportance.MainArticle), Is.EqualTo(3));
            Assert.That(source.Clues.Count(clue => clue.Importance == NewspaperImportance.ShortReport), Is.EqualTo(3));
            foreach (var clue in source.Clues)
            {
                Assert.That(source.CreateRun().Count(item => item.ItemType == clue.ItemType), Is.EqualTo(1));
                Assert.That(source.Database.GetVariantsByType(clue.ItemType), Has.Count.EqualTo(2));
            }
        }

        [UnityTest]
        public IEnumerator PreparationAndAuctionShareOneModalNewspaper()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/AuctionDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            Assert.That(auction, Is.Not.Null);
            Assert.That(preparation, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            var starts = new List<AuctionItem>();
            auction.OnItemChanged += starts.Add;

            Assert.That(GameObject.Find("Starting budget").GetComponent<TMP_Text>().text, Does.Contain("$1,000"));
            Assert.That(GameObject.Find("Profit target").GetComponent<TMP_Text>().text, Does.Contain("$300"));
            Assert.That(GameObject.Find("Brief").GetComponent<TMP_Text>().text, Is.EqualTo(
                "Good evening, agent. You have $1,000. I expect at least $300 profit. Choose carefully."));

            var newspaper = preparation.GetComponentInChildren<NewspaperView>(true);
            Click("Open Newspaper");
            Assert.That(newspaper.IsOpen, Is.True);
            var scroll = newspaper.GetComponentInChildren<ScrollRect>();
            var articles = scroll.content.GetComponentsInChildren<TMP_Text>().Select(label => label.text).ToArray();
            var expected = new[] { "Main articles" }
                .Concat(ArticleText(level, NewspaperImportance.MainArticle))
                .Append("Short reports")
                .Concat(ArticleText(level, NewspaperImportance.ShortReport));
            CollectionAssert.AreEqual(expected, articles);
            CollectionAssert.AreEqual(new[] { "Newspaper", "Close Newspaper" }.Concat(articles),
                newspaper.GetComponentsInChildren<TMP_Text>().Select(label => label.text));
            Assert.That(scroll.content.rect.height,
                Is.GreaterThanOrEqualTo(scroll.content.GetComponentsInChildren<TMP_Text>().Sum(label => label.preferredHeight)));
            Click("Close Newspaper");
            Assert.That(newspaper.IsOpen, Is.False);

            var start = GameObject.Find("Start Auction").GetComponent<Button>();
            start.onClick.Invoke();
            start.onClick.Invoke();
            Assert.That(starts, Has.Count.EqualTo(1));
            Assert.That(auction.CurrentItem, Is.SameAs(level.CreateRun()[0]));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(level.StartingBudget));

            auction.Tick(AuctionManager.RevealDuration);
            var bid = GameObject.Find("BID Button").GetComponent<Button>();
            Assert.That(bid.IsInteractable(), Is.True);
            var events = EventSystem.current;
            events.SetSelectedGameObject(bid.gameObject);
            float remaining = auction.RemainingTime;
            Click("Auction Newspaper");
            Assert.That(auction.RemainingTime, Is.EqualTo(remaining));
            Assert.That(newspaper.IsOpen, Is.True);
            CollectionAssert.AreEqual(articles, scroll.content.GetComponentsInChildren<TMP_Text>().Select(label => label.text));
            Assert.That(events.currentSelectedGameObject.name, Is.EqualTo("Close Newspaper"));

            Canvas.ForceUpdateCanvases();
            var bidRect = (RectTransform)bid.transform;
            var pointer = RectTransformUtility.WorldToScreenPoint(null, bidRect.TransformPoint(bidRect.rect.center));
            Assert.That(bid.targetGraphic.Raycast(pointer, null), Is.False);
            Assert.That(bid.IsInteractable(), Is.False);
            Assert.That(auction.HighestBidderId, Is.Empty);

            float timeScale = Time.timeScale;
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(auction.RemainingTime, Is.LessThan(remaining));
            Assert.That(auction.CurrentItem, Is.SameAs(level.CreateRun()[0]));
            remaining = auction.RemainingTime;
            Click("Close Newspaper");
            Assert.That(auction.RemainingTime, Is.EqualTo(remaining));
            Assert.That(newspaper.IsOpen, Is.False);
            Assert.That(events.currentSelectedGameObject, Is.SameAs(bid.gameObject));
            Assert.That(bid.targetGraphic.Raycast(pointer, null), Is.True);
            Assert.That(bid.IsInteractable(), Is.True);

            preparation.OpenNewspaper();
            auction.Tick(10f);
            Assert.That(newspaper.IsOpen, Is.True);
            Assert.That(auction.BidHistory.Any(record => record.bidderId != BidderIds.Player), Is.True);
            auction.Tick(AuctionManager.MaximumBiddingDuration);
            Assert.That(auction.Results, Is.Not.Empty);
            Assert.That(auction.CurrentItem, Is.Not.SameAs(level.CreateRun()[0]));
            Assert.That(newspaper.IsOpen, Is.True);
            CollectionAssert.AreEqual(articles, scroll.content.GetComponentsInChildren<TMP_Text>().Select(label => label.text));
            preparation.CloseNewspaper();

            for (int frame = 0; frame < 16 && auction.State != AuctionState.Bidding; frame++)
                auction.Tick(0.25f);
            while (auction.State == AuctionState.Bidding && auction.RemainingTime > AuctionManager.AntiSnipingTime)
                auction.Tick(0.25f);

            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.RemainingTime, Is.InRange(0.01f, AuctionManager.AntiSnipingTime));
            var lateLot = auction.CurrentItem;
            int settled = auction.Results.Count;
            Click("Auction Newspaper");
            Assert.That(newspaper.IsOpen, Is.True);
            yield return null;
            Assert.That(auction.CurrentItem, Is.SameAs(lateLot));
            auction.Tick(AuctionManager.MaximumBiddingDuration);
            Assert.That(auction.Results.Count, Is.GreaterThan(settled));
            Assert.That(newspaper.IsOpen, Is.True);
            Click("Close Newspaper");
            yield return new ExitPlayMode();
        }

        private static void Click(string name) => GameObject.Find(name).GetComponent<Button>().onClick.Invoke();

        private static IEnumerable<string> ArticleText(MvpAuctionLevel level, NewspaperImportance importance) =>
            level.Clues.Where(clue => clue.Importance == importance)
                .SelectMany(clue => new[] { clue.Headline, clue.Body });
    }
}
