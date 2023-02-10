using System.Diagnostics;
using NUnit.Framework;


// TODO: get console output working from tests
// https://docs.nunit.org/articles/vs-test-adapter/Trace-and-Debug.html
[SetUpFixture]
public class SetupTrace {
    [OneTimeSetUp]
    public void StartTest()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    [OneTimeTearDown]
    public void EndTest()
    {
        Trace.Flush();
    }
}