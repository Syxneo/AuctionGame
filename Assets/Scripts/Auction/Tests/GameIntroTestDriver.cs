using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    internal static class GameIntroTestDriver
    {
        internal static void LoadIntegratedScene(int? dailySeed = null)
        {
            SceneManager.sceneLoaded += ConfigureRun;
            EditorSceneManager.LoadSceneInPlayMode(DailyItemGenerationTests.IntegratedScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));

            void ConfigureRun(Scene scene, LoadSceneMode mode)
            {
                SceneManager.sceneLoaded -= ConfigureRun;
                var preparation = Object.FindFirstObjectByType<PreparationPhase>();
                var data = new SerializedObject(preparation);
                if (!data.FindProperty("useValueCappedGeneration").boolValue)
                    throw new System.InvalidOperationException("The integrated scene must use daily generation.");
                data.FindProperty("useValueCappedGeneration").boolValue = dailySeed.HasValue;
                data.FindProperty("useFixedDailySeed").boolValue = dailySeed.HasValue;
                data.FindProperty("fixedDailySeed").intValue = dailySeed.GetValueOrDefault();
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static IEnumerator OpenNewsFromIntro()
        {
            var intro = Object.FindFirstObjectByType<GameIntroFlow>();
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.StartScreen));
            Assert.That(GameObject.Find("NEWSSSS presentation"), Is.Null);
            Assert.That(GameObject.Find("SALE briefing"), Is.Null);
            var play = GameObject.Find("PLAY").GetComponent<Button>();
            play.onClick.Invoke();
            play.onClick.Invoke();
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.IncomingCall));

            AssertSprite("Incoming Telephone");
            var source = intro.GetComponent<AudioSource>();
            Assert.That(source.clip, Is.Not.Null);
            Assert.That(source.loop, Is.True);

            Click("Pick Up Phone");
            Assert.That(source.loop, Is.False);
            AssertSprite("Picked Up Telephone");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.SaleConversation));
            var messages = GameObject.Find("Messages").transform;
            Assert.That(messages.childCount, Is.Zero);
            for (int count = 1; count <= 3; count++)
            {
                Click("Advance Conversation");
                Assert.That(messages.childCount, Is.EqualTo(count));
            }

            var data = new SerializedObject(intro.GetComponent<PreparationPhase>());
            var level = (MvpAuctionLevel)data.FindProperty("level").objectReferenceValue;
            string finalMessage = messages.GetChild(2).GetComponentInChildren<TMP_Text>().text;
            var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.That(finalMessage, Does.Contain("$" + level.StartingBudget.ToString("N0", culture)));
            Assert.That(finalMessage, Does.Contain("$" + level.ProfitTarget.ToString("N0", culture)));
            Assert.That(intro.GetComponents<AudioSource>(), Has.Length.EqualTo(1));

            Click("Advance Conversation");
            Click("Advance Conversation");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.TransitionToNews));
            float deadline = Time.realtimeSinceStartup + 5;
            while (intro.CurrentStage != GameIntroFlow.Stage.Completed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.Completed));
            Assert.That(GameObject.Find("Continue News"), Is.Not.Null);
            AssertSprite("Television");
            AssertSprite("Reporter");
            Assert.That(GameObject.Find("SALE briefing"), Is.Null);
        }

        private static void AssertSprite(string name)
        {
            var target = GameObject.Find(name);
            Assert.That(target, Is.Not.Null, name);
            Assert.That(target.GetComponent<Image>().sprite, Is.Not.Null, name);
        }

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
