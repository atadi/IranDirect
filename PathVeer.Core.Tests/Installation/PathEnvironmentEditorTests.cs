using PathVeer.Core.Installation;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 36.7 — PATH mutation safety.
///
/// Repeated installs must not grow the machine PATH, and uninstall must remove
/// only PathVeer's own entry. These are pure string operations precisely so
/// they can be proven without touching the real environment.
/// </summary>
public sealed class PathEnvironmentEditorTests
{
    private const string Entry = @"C:\Program Files\PathVeer\Cli";

    [Fact]
    public void AddsTheEntryWhenAbsent()
    {
        string result = PathEnvironmentEditor.AddEntry(
            @"C:\Windows;C:\Windows\System32",
            Entry);

        Assert.Equal(
            @"C:\Windows;C:\Windows\System32;C:\Program Files\PathVeer\Cli",
            result);
    }

    [Fact]
    public void AddingTwiceDoesNotDuplicate()
    {
        string once = PathEnvironmentEditor.AddEntry(@"C:\Windows", Entry);
        string twice = PathEnvironmentEditor.AddEntry(once, Entry);
        string thrice = PathEnvironmentEditor.AddEntry(twice, Entry);

        Assert.Equal(once, twice);
        Assert.Equal(once, thrice);

        Assert.Single(
            thrice.Split(';'),
            segment => segment.Contains(
                "PathVeer",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddIsCaseInsensitiveAndTrailingSlashInsensitive()
    {
        string current = @"C:\Windows;c:\program files\pathveer\cli\";

        string result = PathEnvironmentEditor.AddEntry(current, Entry);

        Assert.Equal(current, result);
    }

    [Fact]
    public void AddsToAnEmptyOrNullPath()
    {
        Assert.Equal(Entry, PathEnvironmentEditor.AddEntry(null, Entry));
        Assert.Equal(Entry, PathEnvironmentEditor.AddEntry(string.Empty, Entry));
    }

    [Fact]
    public void RemovesOnlyTheOwnEntry()
    {
        string current =
            @"C:\Windows;C:\Program Files\PathVeer\Cli;C:\Tools;C:\Other";

        string result = PathEnvironmentEditor.RemoveEntry(current, Entry);

        Assert.Equal(@"C:\Windows;C:\Tools;C:\Other", result);
    }

    [Fact]
    public void RemoveDoesNotTouchUnrelatedEntriesThatMerelyContainTheName()
    {
        // A user directory that happens to mention PathVeer must survive.
        string current =
            @"C:\Windows;C:\Users\dev\PathVeer-scripts;C:\Program Files\PathVeer\Cli";

        string result = PathEnvironmentEditor.RemoveEntry(current, Entry);

        Assert.Equal(@"C:\Windows;C:\Users\dev\PathVeer-scripts", result);
    }

    [Fact]
    public void RemoveIsIdempotent()
    {
        string current = @"C:\Windows;C:\Tools";

        Assert.Equal(
            current,
            PathEnvironmentEditor.RemoveEntry(current, Entry));
    }

    [Fact]
    public void RemoveClearsEveryDuplicateLeftByHistoricalEdits()
    {
        string current =
            $@"C:\Windows;{Entry};C:\Tools;{Entry}";

        string result = PathEnvironmentEditor.RemoveEntry(current, Entry);

        Assert.Equal(@"C:\Windows;C:\Tools", result);
    }

    [Fact]
    public void EmptySegmentsAreNotResurrected()
    {
        string result = PathEnvironmentEditor.AddEntry(
            @"C:\Windows;;;C:\Tools;",
            Entry);

        Assert.DoesNotContain(";;", result);
        Assert.False(result.EndsWith(';'));
    }

    [Fact]
    public void AddThenRemoveRestoresTheOriginalPath()
    {
        string original = @"C:\Windows;C:\Windows\System32;C:\Tools";

        string added = PathEnvironmentEditor.AddEntry(original, Entry);
        string removed = PathEnvironmentEditor.RemoveEntry(added, Entry);

        Assert.Equal(original, removed);
    }

    [Fact]
    public void ContainsEntryDetectsPresenceRegardlessOfSpelling()
    {
        Assert.True(PathEnvironmentEditor.ContainsEntry(
            @"C:\Windows;C:\PROGRAM FILES\PATHVEER\CLI",
            Entry));

        Assert.False(PathEnvironmentEditor.ContainsEntry(
            @"C:\Windows;C:\Tools",
            Entry));
    }

    [Fact]
    public void OrderOfUnrelatedEntriesIsPreserved()
    {
        string current = @"C:\A;C:\B;C:\C;C:\D";

        string added = PathEnvironmentEditor.AddEntry(current, Entry);

        Assert.StartsWith(current, added);
    }
}
