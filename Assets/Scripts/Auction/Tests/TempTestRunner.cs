using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AuctionGame.Auction.Tests
{
    internal static class TempTestRunner
    {
        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => Debug.Log("TEMP RUN STARTED");

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"TEMP RUN FINISHED passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount}");
                LogChildren(result);
            }

            private static void LogChildren(ITestResultAdaptor r)
            {
                if (r.TestStatus == TestStatus.Failed)
                    Debug.LogError("TEMP TEST FAILED: " + r.FullName + " :: " + r.Message);
                foreach (ITestResultAdaptor child in r.Children)
                    LogChildren(child);
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        [MenuItem("Tools/Temp/Run EditMode Tests")]
        public static void RunEditMode()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        [MenuItem("Tools/Temp/Run PlayMode Tests")]
        public static void RunPlayMode()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode }));
        }
    }
}
