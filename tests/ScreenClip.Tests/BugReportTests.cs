using ScreenClip.Core;

namespace ScreenClip.Tests;

public class BugReportTests
{
    [Fact]
    public void TheUsersFolderAndNames_AreHidden()
    {
        string text = @"Could not save C:\Users\Quentin\Pictures\a.png (QUENTIN on DESKTOP-42); QuentinPerou stays";
        string clean = BugReport.Anonymize(text, @"C:\Users\Quentin\", "Quentin", "DESKTOP-42");
        Assert.Equal(@"Could not save %USERPROFILE%\Pictures\a.png (<user> on <computer>); QuentinPerou stays", clean);
    }

    [Fact]
    public void NothingToHide_LeavesTheText()
    {
        Assert.Equal("at ScreenClip.App.OnStartup()", BugReport.Anonymize("at ScreenClip.App.OnStartup()", null, "a", ""));
    }

    [Fact]
    public void AShortReport_OpensWhole()
    {
        string url = BugReport.NewIssueUrl("me/app", "Problem", "Head\n", "line 1\nline 2\n", "Tail", "(cut)", out bool cut);
        Assert.False(cut);
        Assert.StartsWith("https://github.com/me/app/issues/new?labels=bug&title=Problem&body=", url);
        Assert.Contains(Uri.EscapeDataString("line 2"), url);
        Assert.Contains(Uri.EscapeDataString("Tail"), url);
    }

    [Fact]
    public void ALongReport_IsCutAtALine_ItsEndKept()
    {
        string details = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"log line {i} with some words in it"));
        string url = BugReport.NewIssueUrl("me/app", "Problem", "Head\n", details, "</details>", "(cut)", out bool cut, maxLength: 3000);
        Assert.True(cut);
        Assert.True(url.Length <= 3000);
        Assert.Contains(Uri.EscapeDataString("log line 1 "), url);
        Assert.DoesNotContain(Uri.EscapeDataString("log line 400"), url);
        Assert.Contains(Uri.EscapeDataString("(cut)"), url);
        Assert.EndsWith(Uri.EscapeDataString("</details>"), url);
    }
}
