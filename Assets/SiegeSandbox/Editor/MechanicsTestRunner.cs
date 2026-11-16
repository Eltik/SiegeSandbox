using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SiegeSandbox.EditorTools
{
    /// <summary>
    /// Siege Sandbox > Run Mechanics Tests runs the play mode tests and writes a
    /// plain-text summary to Logs/mechanics-tests.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class MechanicsTestRunner
    {
        const string ResultPath = "Logs/mechanics-tests.txt";
        static readonly TestRunnerApi api;

        static MechanicsTestRunner()
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.hideFlags = HideFlags.HideAndDontSave;
            api.RegisterCallbacks(new ResultWriter());
        }

        [MenuItem("Siege Sandbox/Run Mechanics Tests")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ResultPath, "RUNNING\n");
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode }));
        }

        class ResultWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FINISHED " + result.TestStatus + "  passed=" + result.PassCount
                    + " failed=" + result.FailCount + " skipped=" + result.SkipCount);
                Append(result, sb);
                Directory.CreateDirectory("Logs");
                File.WriteAllText(ResultPath, sb.ToString());
            }

            static void Append(ITestResultAdaptor r, StringBuilder sb)
            {
                if (!r.HasChildren)
                {
                    sb.AppendLine(r.TestStatus + "  " + r.Test.Name
                        + (string.IsNullOrEmpty(r.Message) ? "" : "  :: " + r.Message));
                    return;
                }
                foreach (ITestResultAdaptor child in r.Children) Append(child, sb);
            }
        }
    }
}
