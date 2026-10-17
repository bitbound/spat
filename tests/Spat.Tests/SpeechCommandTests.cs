using Spat.Libraries.Core.Prompts;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.SpeechCommands;

namespace Spat.Tests;

public class SpeechCommandServiceTests
{
    [Fact]
    public void GetBuiltIns_ReturnsTheShippedCommandsSwitchedOn()
    {
        var commands = Create(new InMemoryFileStore()).GetBuiltIns();

        Assert.Equal(["quote", "pascal", "camel", "snake", "kebab"], commands.Select(command => command.StartPhrase));
        Assert.All(commands, command => Assert.True(command.Enabled));
        Assert.All(commands, command => Assert.True(command.IsBuiltIn));
        Assert.All(commands, command => Assert.NotEmpty(command.EndPhrases));
    }

    [Fact]
    public void GetBuiltIns_WithADisabledIdInSettings_SwitchesOnlyThatCommandOff()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore, new AppSettings
        {
            SpeechCommands = new SpeechCommandsSettings { DisabledBuiltInIds = [BuiltInSpeechCommands.All[1].Id] },
        });

        var commands = new SpeechCommandService(new TestPlatformPaths(), fileStore, settings).GetBuiltIns();

        Assert.False(commands[1].Enabled);
        Assert.True(commands[0].Enabled);
    }

    [Fact]
    public async Task SaveAllAsync_RoundTripsTheCustomCommands()
    {
        var fileStore = new InMemoryFileStore();
        var ct = TestContext.Current.CancellationToken;
        var commands = Create(fileStore);

        await commands.SaveAllAsync(
            [
                new SpeechCommand
                {
                    StartPhrase = "shout",
                    EndPhrases = ["end shout"],
                    Instructions = "Write the enclosed words in upper case.",
                },
            ],
            ct);

        var stored = Assert.Single(Create(fileStore).GetCustom());

        Assert.Equal("shout", stored.StartPhrase);
        Assert.Equal(["end shout"], stored.EndPhrases);
        Assert.Equal("Write the enclosed words in upper case.", stored.Instructions);
        Assert.False(stored.IsBuiltIn);

        // The editing and display projections must not reach the file, or a reload would rebuild the
        // phrases from them instead of from the stored list.
        Assert.DoesNotContain("endPhrasesText", fileStore.ReadAllText(new TestPlatformPaths().SpeechCommandsFilePath));
        Assert.DoesNotContain("\"name\"", fileStore.ReadAllText(new TestPlatformPaths().SpeechCommandsFilePath));
    }

    [Fact]
    public async Task SaveAllAsync_TrimsThePhrasesAndDropsTheEmptyOnes()
    {
        var fileStore = new InMemoryFileStore();
        var ct = TestContext.Current.CancellationToken;

        await Create(fileStore).SaveAllAsync(
            [
                new SpeechCommand
                {
                    StartPhrase = "  shout  ",
                    EndPhrases = ["  end shout  ", "   "],
                    Instructions = "  Write them in upper case.  ",
                },
            ],
            ct);

        var stored = Assert.Single(Create(fileStore).GetCustom());

        Assert.Equal("shout", stored.StartPhrase);
        Assert.Equal(["end shout"], stored.EndPhrases);
        Assert.Equal("Write them in upper case.", stored.Instructions);
    }

    [Fact]
    public async Task SaveAllAsync_DropsCommandsThatAreMissingAPhraseOrInstructions()
    {
        var fileStore = new InMemoryFileStore();
        var ct = TestContext.Current.CancellationToken;

        await Create(fileStore).SaveAllAsync(
            [
                new SpeechCommand { StartPhrase = "shout", Instructions = "Upper case." },
                new SpeechCommand { StartPhrase = "whisper", EndPhrases = ["end whisper"] },
                new SpeechCommand { EndPhrases = ["end calm"], Instructions = "Lower case." },
                new SpeechCommand { StartPhrase = "calm", EndPhrases = ["end calm"], Instructions = "Lower case." },
            ],
            ct);

        var stored = Assert.Single(Create(fileStore).GetCustom());

        Assert.Equal("calm", stored.StartPhrase);
    }

    [Fact]
    public async Task SaveAllAsync_KeepsTheBuiltInCommandsOutOfTheFile()
    {
        var fileStore = new InMemoryFileStore();
        var ct = TestContext.Current.CancellationToken;

        await Create(fileStore).SaveAllAsync(
            [
                new SpeechCommand
                {
                    Id = BuiltInSpeechCommands.All[0].Id,
                    StartPhrase = "quote",
                    EndPhrases = ["end quote"],
                    Instructions = "Someone edited the shipped command.",
                    IsBuiltIn = true,
                },
            ],
            ct);

        Assert.Empty(Create(fileStore).GetCustom());
    }

    [Fact]
    public void BuildInstructionBlock_WithEveryCommandEnabled_ListsTheShippedCommands()
    {
        var block = Create(new InMemoryFileStore()).BuildInstructionBlock();

        Assert.NotNull(block);
        Assert.Contains("Opens with \"quote\" and closes with \"end quote\"", block);
        Assert.Contains("removing the spaces between them", block);
    }

    [Fact]
    public void BuildInstructionBlock_LeavesOutADisabledCommand()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore, new AppSettings
        {
            SpeechCommands = new SpeechCommandsSettings { DisabledBuiltInIds = [BuiltInSpeechCommands.All[0].Id] },
        });

        var block = new SpeechCommandService(new TestPlatformPaths(), fileStore, settings).BuildInstructionBlock();

        Assert.NotNull(block);
        Assert.DoesNotContain("\"quote\"", block);
        Assert.Contains("\"pascal\"", block);
    }

    [Fact]
    public void BuildInstructionBlock_WithEveryCommandDisabled_ReturnsNull()
    {
        var fileStore = new InMemoryFileStore();
        var settings = TestSettings.Create(fileStore, new AppSettings
        {
            SpeechCommands = new SpeechCommandsSettings
            {
                DisabledBuiltInIds = [.. BuiltInSpeechCommands.All.Select(command => command.Id)],
            },
        });

        Assert.Null(new SpeechCommandService(new TestPlatformPaths(), fileStore, settings).BuildInstructionBlock());
    }

    [Fact]
    public async Task BuildInstructionBlock_IncludesTheCustomCommandsAfterTheShippedOnes()
    {
        var fileStore = new InMemoryFileStore();
        var commands = Create(fileStore);
        var ct = TestContext.Current.CancellationToken;

        await commands.SaveAllAsync(
            [
                new SpeechCommand
                {
                    StartPhrase = "shout",
                    EndPhrases = ["end shout", "stop shouting"],
                    Instructions = "Write the enclosed words in upper case.",
                },
            ],
            ct);

        var block = commands.BuildInstructionBlock();

        Assert.NotNull(block);
        Assert.Contains("Opens with \"shout\" and closes with \"end shout\" or \"stop shouting\"", block);
        Assert.True(
            block.IndexOf("Opens with \"quote\"", StringComparison.Ordinal)
            < block.IndexOf("Opens with \"shout\"", StringComparison.Ordinal));
    }

    private static SpeechCommandService Create(InMemoryFileStore fileStore)
    {
        return new SpeechCommandService(new TestPlatformPaths(), fileStore, TestSettings.Create(fileStore));
    }
}

public class SpeechCommandTests
{
    [Fact]
    public void Name_CapitalizesTheStartPhrase()
    {
        Assert.Equal("Quote", new SpeechCommand { StartPhrase = "quote" }.Name);
        Assert.Equal("End", new SpeechCommand { StartPhrase = "End" }.Name);
    }

    [Fact]
    public void Name_WithoutAStartPhrase_IsEmpty()
    {
        Assert.Equal(string.Empty, new SpeechCommand().Name);
    }

    [Fact]
    public void EndPhrasesText_KeepsTheBlankLinesSoTypingCanContinue()
    {
        var command = new SpeechCommand { EndPhrasesText = "end shout\n" };

        Assert.Equal("end shout\n", command.EndPhrasesText);
    }

    [Fact]
    public void EndPhrasesText_SplitsTheLinesIntoPhrases()
    {
        var command = new SpeechCommand { EndPhrasesText = "end shout\nstop shouting" };

        Assert.Equal(["end shout", "stop shouting"], command.EndPhrases);
    }
}

public class PromptRendererSpeechCommandTests
{
    [Fact]
    public void Render_WithTheSpeechCommandsPlaceholder_ReplacesIt()
    {
        var rendered = PromptRenderer.Render("Commands: ${speech_commands}\n\nFix: ${stt_output}", "raw", null, "- Opens with \"quote\" and closes with \"end quote\". Wrap them.");

        Assert.StartsWith("Commands: - Opens with \"quote\" and closes with \"end quote\". Wrap them.\n\nFix: ", rendered.SystemPrompt);
        Assert.DoesNotContain("raw", rendered.SystemPrompt);
    }

    [Fact]
    public void Render_WithoutThePlaceholder_PutsTheCommandsAheadOfTheDictionaryBlock()
    {
        var rendered = PromptRenderer.Render("Fix: ${stt_output}", "raw", "- \"a\" -> \"B\"", "Commands.");

        Assert.Equal($"Fix: Commands.\n\n- \"a\" -> \"B\"\n\n{PromptRenderer.OutputPointer}", rendered.SystemPrompt);
    }

    [Fact]
    public void Render_WithNoCommandsBlock_LeavesThePlaceholderEmpty()
    {
        var rendered = PromptRenderer.Render("Commands: ${speech_commands}\n\nFix: ${stt_output}", "raw", null, speechCommandsBlock: null);

        Assert.Equal($"Commands: \n\nFix: {PromptRenderer.OutputPointer}", rendered.SystemPrompt);
    }
}
