using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Kairos.Editor
{
    public static class DemoTools
    {
        [MenuItem("KAIROS/Open Demo")]
        public static void OpenDemo()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene("Assets/Kairos/Scenes/KairosDemo.unity");
        }
        [MenuItem("KAIROS/Sync Card Data")]
        public static void SyncCards()
        {
            string data = File.ReadAllText("docs/data/cards.json"); Kairos.Engine.CardDatabase.Load(data);
            File.WriteAllText("Assets/Kairos/Data/cards.json", data);
            AssetDatabase.ImportAsset("Assets/Kairos/Data/cards.json"); Debug.Log("KAIROS: validated and synchronized card data.");
        }
        [MenuItem("KAIROS/Run Engine Tests")]
        public static void RunTests()
        {
            if (TestBridge.Running) return;
            TestBridge.Run();
        }
    }
    // Bridge to the official Unity Test Runner, also usable by automation.
    public sealed class TestBridge : ICallbacks
    {
        static TestRunnerApi api;
        public static bool Running;
        public static int Passed, Failed, Total;
        public static readonly List<string> Failures = new List<string>();
        public static void Run()
        {
            Running = true; Passed = Failed = Total = 0; Failures.Clear();
            if (api == null) api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new TestBridge());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Kairos.Engine.Tests" } }));
        }
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.Test.IsSuite) return;
            Total++; if (result.TestStatus == TestStatus.Passed) Passed++;
            if (result.TestStatus == TestStatus.Failed) { Failed++; Failures.Add(result.Test.FullName + ": " + result.Message); }
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            Running = false;
            string path = "Library/Locus/tmp/kairos-editmode-results.txt"; Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "Passed=" + Passed + "; Failed=" + Failed + "; Total=" + Total + "\n" + string.Join("\n", Failures));
            api.UnregisterCallbacks(this); Debug.Log("KAIROS EditMode tests: " + Passed + "/" + Total + " passed; failed=" + Failed);
        }
    }
}
