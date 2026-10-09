using Spat.Libraries.Core.CustomDictionary;
using Spat.Libraries.Core.Prompts;

namespace Spat.Tests;

public class CustomDictionaryServiceTests
{
    [Fact]
    public async Task CreateAsync_RoundTripsThroughStorage()
    {
        var fileStore = new InMemoryFileStore();
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), fileStore);
        var ct = TestContext.Current.CancellationToken;

        await dictionary.CreateAsync("control are", "ControlR", ct);

        var reloaded = new CustomDictionaryService(new TestPlatformPaths(), fileStore);
        var entry = Assert.Single(reloaded.GetAll());

        Assert.Equal("control are", entry.From);
        Assert.Equal("ControlR", entry.To);
        Assert.True(entry.Enabled);
    }

    [Fact]
    public async Task CreateAsync_TrimmsThePhraseAndTheTerm()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());

        var entry = await dictionary.CreateAsync("  control are  ", "  ControlR  ", TestContext.Current.CancellationToken);

        Assert.Equal("control are", entry.From);
        Assert.Equal("ControlR", entry.To);
    }

    [Fact]
    public async Task CreateAsync_WithABlankPhraseOrTerm_Throws()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => dictionary.CreateAsync("  ", "ControlR", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => dictionary.CreateAsync("control are", " ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAllAsync_DropsIncompleteRows()
    {
        var fileStore = new InMemoryFileStore();
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), fileStore);
        var ct = TestContext.Current.CancellationToken;

        await dictionary.SaveAllAsync(
        [
            new CustomDictionaryEntry { From = "control are", To = "ControlR" },
            new CustomDictionaryEntry { From = "half", To = "  " },
            new CustomDictionaryEntry { From = "", To = "gone" },
        ], ct);

        var entry = Assert.Single(dictionary.GetAll());

        Assert.Equal("ControlR", entry.To);
    }

    [Fact]
    public async Task SaveAllAsync_KeepsExistingIdsAcrossRewrites()
    {
        var fileStore = new InMemoryFileStore();
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), fileStore);
        var ct = TestContext.Current.CancellationToken;

        var created = await dictionary.CreateAsync("control are", "ControlR", ct);
        created.To = "ControlR App";
        await dictionary.SaveAllAsync([created], ct);

        var entry = Assert.Single(dictionary.GetAll());

        Assert.Equal(created.Id, entry.Id);
        Assert.Equal("ControlR App", entry.To);
    }

    [Fact]
    public async Task SaveAllAsync_PreservesEntryOrder()
    {
        var fileStore = new InMemoryFileStore();
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), fileStore);
        var ct = TestContext.Current.CancellationToken;

        await dictionary.SaveAllAsync(
        [
            new CustomDictionaryEntry { From = "top", To = "First" },
            new CustomDictionaryEntry { From = "middle", To = "Second" },
        ], ct);
        await dictionary.SaveAllAsync(
        [
            new CustomDictionaryEntry { From = "middle", To = "Second" },
            new CustomDictionaryEntry { From = "top", To = "First" },
        ], ct);

        var reloaded = new CustomDictionaryService(new TestPlatformPaths(), fileStore);

        Assert.Equal(["middle", "top"], reloaded.GetAll().Select(entry => entry.From));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheSelectedEntry()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());
        var ct = TestContext.Current.CancellationToken;

        var keep = await dictionary.CreateAsync("keep this", "Keep", ct);
        var drop = await dictionary.CreateAsync("drop this", "Drop", ct);

        Assert.True(await dictionary.DeleteAsync(drop.Id, ct));

        var entry = Assert.Single(dictionary.GetAll());
        Assert.Equal(keep.Id, entry.Id);
    }

    [Fact]
    public async Task DeleteAsync_WithAnUnknownId_ReturnsFalse()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());

        await dictionary.CreateAsync("control are", "ControlR", TestContext.Current.CancellationToken);

        Assert.False(await dictionary.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAll_WithACorruptFile_FallsBackToNoEntries()
    {
        var fileStore = new InMemoryFileStore();
        var paths = new TestPlatformPaths();

        await fileStore.WriteAllTextAsync(paths.DictionaryFilePath, "{ not json", TestContext.Current.CancellationToken);

        var dictionary = new CustomDictionaryService(paths, fileStore);

        Assert.Empty(dictionary.GetAll());
    }

    [Fact]
    public async Task BuildHintPrompt_ListsDistinctTermsAndRespectsTheDisabledFlag()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());
        var ct = TestContext.Current.CancellationToken;

        await dictionary.CreateAsync("control are", "ControlR", ct);
        await dictionary.CreateAsync("control er", "ControlR", ct);
        var disabled = await dictionary.CreateAsync("spat", "Spat", ct);
        disabled.Enabled = false;
        await dictionary.UpdateAsync(disabled, ct);

        Assert.Equal("ControlR", dictionary.BuildHintPrompt());
    }

    [Fact]
    public void BuildHintPrompt_WithNoEntries_ReturnsNull()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());

        Assert.Null(dictionary.BuildHintPrompt());
    }

    [Fact]
    public async Task BuildCorrectionBlock_ListsEnabledEntriesAsLines()
    {
        var dictionary = new CustomDictionaryService(new TestPlatformPaths(), new InMemoryFileStore());
        var ct = TestContext.Current.CancellationToken;

        await dictionary.CreateAsync("control are", "ControlR", ct);
        var off = await dictionary.CreateAsync("skip", "Skipped", ct);
        off.Enabled = false;
        await dictionary.UpdateAsync(off, ct);

        var block = dictionary.BuildCorrectionBlock();

        Assert.NotNull(block);
        Assert.Contains("- \"control are\" -> \"ControlR\"", block);
        Assert.DoesNotContain("Skipped", block);
    }

    [Fact]
    public async Task SaveAsync_WritesWithOwnerOnlyPermissions()
    {
        var fileStore = new InMemoryFileStore();
        var paths = new TestPlatformPaths();
        var dictionary = new CustomDictionaryService(paths, fileStore);

        await dictionary.CreateAsync("control are", "ControlR", TestContext.Current.CancellationToken);

        Assert.Contains(paths.DictionaryFilePath, fileStore.RestrictedPaths);
    }
}

public class PhraseReplacerTests
{
    private static string Replace(string text, params (string From, string To)[] entries)
    {
        return PhraseReplacer.Replace(text, entries.Select(entry => new CustomDictionaryEntry
        {
            From = entry.From,
            To = entry.To,
        }));
    }

    [Fact]
    public void Replace_MatchesCaseInsensitively()
    {
        var result = Replace("Open Control ARE now", ("control are", "ControlR"));

        Assert.Equal("Open ControlR now", result);
    }

    [Fact]
    public void Replace_DoesNotFireInsideWords()
    {
        var result = Replace("this area is fine", ("are", "ControlR"));

        Assert.Equal("this area is fine", result);
    }

    [Fact]
    public void Replace_MatchesWholePhrasesAcrossPunctuationBoundaries()
    {
        var result = Replace("say control are, please", ("control are", "ControlR"));

        Assert.Equal("say ControlR, please", result);
    }

    [Fact]
    public void Replace_WhenPhrasesOverlap_TheEarlierEntryWins()
    {
        var result = Replace("control are", ("control", "CTRL"), ("control are", "ControlR"));

        Assert.Equal("CTRL are", result);
    }

    [Fact]
    public void Replace_WhenTheLongerPhraseComesFirst_TheWholePhraseMatches()
    {
        var result = Replace("control are", ("control are", "ControlR"), ("control", "CTRL"));

        Assert.Equal("ControlR", result);
    }

    [Fact]
    public void Replace_WhenTwoEntriesShareAPhrase_TheEarlierTermWins()
    {
        var result = Replace("control are", ("control are", "ControlR"), ("control are", "Control Runner"));

        Assert.Equal("ControlR", result);
    }

    [Fact]
    public void Replace_NeverScansReplacementText()
    {
        var result = Replace("control are", ("control are", "control r"), ("control r", "ChainR"));

        Assert.Equal("control r", result);
    }

    [Fact]
    public void Replace_ReplacesEveryOccurrence()
    {
        var result = Replace("are are are", ("are", "R"));

        Assert.Equal("R R R", result);
    }

    [Fact]
    public void Replace_SkipsDisabledEntries()
    {
        var entries = new List<CustomDictionaryEntry>
        {
            new() { From = "control are", To = "ControlR", Enabled = false },
        };

        Assert.Equal("control are", PhraseReplacer.Replace("control are", entries));
    }

    [Fact]
    public void Replace_WithNoUsableEntries_ReturnsTheTextUnchanged()
    {
        var result = PhraseReplacer.Replace("hello",
        [
            new CustomDictionaryEntry { From = "  ", To = "x" },
            new CustomDictionaryEntry { From = "hello", To = "  " },
        ]);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void Replace_MatchesAtTheStartAndEndOfTheText()
    {
        Assert.Equal("ControlR works", Replace("control are works", ("control are", "ControlR")));
        Assert.Equal("works ControlR", Replace("works control are", ("control are", "ControlR")));
    }
}

public class PromptRendererDictionaryTests
{
    [Fact]
    public void Render_WithTheDictionaryPlaceholder_ReplacesIt()
    {
        var rendered = PromptRenderer.Render("Terms: ${dictionary}\n\nFix: ${stt_output}", "raw", "- \"a\" -> \"B\"");

        Assert.Equal("Terms: - \"a\" -> \"B\"\n\nFix: raw", rendered);
    }

    [Fact]
    public void Render_WithoutThePlaceholder_AppendsTheBlock()
    {
        var rendered = PromptRenderer.Render("Fix the grammar.", "raw", "- \"a\" -> \"B\"");

        Assert.Contains("- \"a\" -> \"B\"", rendered);
        Assert.Contains("raw", rendered);
    }

    [Fact]
    public void Render_WithOnlyTheOutputPlaceholder_PutsTheBlockAheadOfTheTranscription()
    {
        var rendered = PromptRenderer.Render("Fix: ${stt_output}", "raw", "- \"a\" -> \"B\"");

        Assert.Equal("Fix: - \"a\" -> \"B\"\n\nraw", rendered);
    }

    [Fact]
    public void Render_WithoutAPlaceholder_PutsTheBlockAheadOfTheTranscription()
    {
        var rendered = PromptRenderer.Render("Fix the grammar.", "raw", "- \"a\" -> \"B\"");

        Assert.Equal("Fix the grammar.\n\n- \"a\" -> \"B\"\n\nTranscription:\nraw", rendered);
    }

    [Fact]
    public void Render_WithTheBuiltInPrompt_PutsTheBlockAheadOfTheTranscription()
    {
        const string block = "- \"control are\" -> \"ControlR\"";

        var rendered = PromptRenderer.Render(TranscriptionPrompt.BuiltInInstructions, "open control are settings", block);

        Assert.Contains(block, rendered);
        Assert.True(
            rendered.IndexOf(block, StringComparison.Ordinal)
            < rendered.IndexOf("open control are settings", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_WithNoBlock_LeavesThePromptAlone()
    {
        var rendered = PromptRenderer.Render("Fix: ${stt_output}", "raw", dictionaryBlock: null);

        Assert.Equal("Fix: raw", rendered);
    }
}
