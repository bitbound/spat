using System.Collections.ObjectModel;
using Spat.Libraries.Core.Prompts;
using Spat.Libraries.Core.Settings;
using Spat.Libraries.Core.Speech;
using Spat.Libraries.Core.SpeechCommands;
using Spat.Views;

namespace Spat.ViewModels;

public sealed partial class PostProcessingViewModel : SettingsPageViewModel<PostProcessingView>
{
    private readonly ISettingsService _settings;
    private readonly IPromptService _prompts;
    private readonly ISpeechCommandService _speechCommands;
    private readonly ITextGenerationClient _textGeneration;

  [ObservableProperty]
  public partial bool PostProcessingEnabled { get; set; }

  [ObservableProperty]
  public partial int PostTimeoutSeconds { get; set; } = PostProcessingSettings.DefaultTimeoutSeconds;
  [ObservableProperty]
  public partial string? PostEndpoint { get; set; }

  [ObservableProperty]
  public partial string? PostApiKey { get; set; }
  [ObservableProperty]
  public partial string? PostModelId { get; set; }

  [ObservableProperty]
    private ObservableCollection<string> _postModels = [];

  [ObservableProperty]
  public partial bool IsLoadingPostModels { get; set; }

  [ObservableProperty]
    private string? _postModelsMessage;

  [ObservableProperty]
  public partial string ThinkingMode { get; set; } = "Not set";

  [ObservableProperty]
    private string _reasoningEffort = "Not set";

  [ObservableProperty]
  public partial string Temperature { get; set; } = string.Empty;

  [ObservableProperty]
    private string _topP = string.Empty;

  [ObservableProperty]
  public partial string MaxCompletionTokens { get; set; } = string.Empty;

  [ObservableProperty]
    private string _maxTokens = string.Empty;

  [ObservableProperty]
  public partial string FrequencyPenalty { get; set; } = string.Empty;

  [ObservableProperty]
    private string _presencePenalty = string.Empty;

  [ObservableProperty]
  public partial string RepetitionPenalty { get; set; } = string.Empty;

  [ObservableProperty]
    private string _seed = string.Empty;

  [ObservableProperty]
  public partial string NChoices { get; set; } = string.Empty;

  [ObservableProperty]
    private string _stopSequence = string.Empty;

  [ObservableProperty]
  public partial string ResponseFormat { get; set; } = string.Empty;

  [ObservableProperty]
    private string _advancedOverridesJson = string.Empty;

  [ObservableProperty]
  public partial ObservableCollection<PromptOption> PromptOptions { get; set; } = [];

  [ObservableProperty]
    private PromptOption? _selectedPrompt;

  [ObservableProperty]
  public partial string PromptTitle { get; set; } = string.Empty;

  [ObservableProperty]
  public partial string PromptInstructions { get; set; } = string.Empty;
  [ObservableProperty]
  public partial bool IsPromptReadOnly { get; set; }

  [ObservableProperty]
  public partial ObservableCollection<SpeechCommand> BuiltInCommands { get; set; } = [];

  [ObservableProperty]
  public partial ObservableCollection<SpeechCommand> CustomCommands { get; set; } = [];

  public PostProcessingViewModel(
      ISettingsService settings,
      IPromptService prompts,
      ISpeechCommandService speechCommands,
      ITextGenerationClient textGeneration)
    {
        _settings = settings;
        _prompts = prompts;
        _speechCommands = speechCommands;
        _textGeneration = textGeneration;
    }

    public IReadOnlyList<string> ThinkingModes { get; } = ["Not set", "Enabled", "Disabled"];

    public IReadOnlyList<string> ReasoningEfforts { get; } =
        ["Not set", "none", "minimal", "low", "medium", "high", "xhigh", "max"];

    public IReadOnlyList<string> ResponseFormats { get; } = [string.Empty, "text", "json_object"];

    protected override Task OnInitializeAsync()
    {
        LoadFromSettings();
        RefreshPrompts();
        RefreshSpeechCommands();

        return Task.CompletedTask;
    }

    partial void OnSelectedPromptChanged(PromptOption? value)
    {
        if (value is null)
        {
            return;
        }

        PromptTitle = value.Prompt.Title;
        PromptInstructions = value.Prompt.Instructions;
        IsPromptReadOnly = value.Prompt.IsBuiltIn;
    }

    [RelayCommand]
    private async Task RefreshPostModelsAsync()
    {
        IsLoadingPostModels = true;
        PostModelsMessage = null;

        try
        {
            var models = await _textGeneration.ListModelsAsync(PostEndpoint, PostApiKey);
            PostModels = new ObservableCollection<string>(models.Select(model => model.Id).Order(StringComparer.Ordinal));
            PostModelsMessage = $"{PostModels.Count} models";
        }
        catch (Exception ex)
        {
            PostModelsMessage = ex.Message;
        }
        finally
        {
            IsLoadingPostModels = false;
        }
    }

    [RelayCommand]
    private async Task CreatePromptAsync()
    {
        var created = await _prompts.CreateAsync("New prompt");

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == created.Id);
    }

    [RelayCommand]
    private async Task DeletePromptAsync()
    {
        if (SelectedPrompt is null || SelectedPrompt.Prompt.IsBuiltIn)
        {
            return;
        }

        await _prompts.DeleteAsync(SelectedPrompt.Prompt.Id);

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault();
    }

    [RelayCommand]
    private async Task DuplicatePromptAsync()
    {
        if (SelectedPrompt is null)
        {
            return;
        }

        var copy = await _prompts.DuplicateAsync(SelectedPrompt.Prompt.Id);

        if (copy is null)
        {
            return;
        }

        RefreshPrompts();
        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == copy.Id);
    }

    [RelayCommand]
    private void Add()
    {
        CustomCommands.Add(new SpeechCommand());
    }

    // A blank or half-filled row is dropped at save, so adding one costs nothing.
    [RelayCommand]
    private void Remove(SpeechCommand? command)
    {
        if (command is not null)
        {
            CustomCommands.Remove(command);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveError = null;
        SaveMessage = null;

        if (!TryReadGenerationOptions(out var options, out var error))
        {
            SaveError = error;
            return;
        }

        var settings = _settings.Current.PostProcessing;

        settings.Enabled = PostProcessingEnabled;
        settings.Endpoint = PostEndpoint?.Trim();
        settings.ApiKey = PostApiKey?.Trim();
        settings.ModelId = PostModelId?.Trim();
        settings.TimeoutSeconds = Math.Clamp(PostTimeoutSeconds, 1, 3600);
        settings.Options = options;
        settings.SelectedPromptId = SelectedPrompt?.Prompt.Id == TranscriptionPrompt.BuiltInId
            ? null
            : SelectedPrompt?.Prompt.Id;

        if (SelectedPrompt is { Prompt.IsBuiltIn: false } prompt)
        {
            prompt.Prompt.Title = PromptTitle;
            prompt.Prompt.Instructions = PromptInstructions;

            await _prompts.UpdateAsync(prompt.Prompt);

            // The dropdown shows the cached title, so it needs rebuilding after a rename.
            RefreshPrompts();
        }

        var speechCommands = _settings.Current.SpeechCommands;
        speechCommands.DisabledBuiltInIds = [.. BuiltInCommands.Where(command => !command.Enabled).Select(command => command.Id)];

        await _speechCommands.SaveAllAsync(CustomCommands);

        // Incomplete commands are dropped at write, so the page has to show what actually survived.
        RefreshSpeechCommands();

        await _settings.SaveAsync();

        ReportSaved();
    }

    private void LoadFromSettings()
    {
        var settings = _settings.Current.PostProcessing;

        PostProcessingEnabled = settings.Enabled;
        PostEndpoint = settings.Endpoint;
        PostApiKey = settings.ApiKey;
        PostModelId = settings.ModelId;
        PostTimeoutSeconds = settings.TimeoutSeconds;

        var options = settings.Options;

        ThinkingMode = options.ThinkingEnabled switch
        {
            null => "Not set",
            true => "Enabled",
            false => "Disabled",
        };
        ReasoningEffort = string.IsNullOrWhiteSpace(options.ReasoningEffort) ? "Not set" : options.ReasoningEffort;
        Temperature = options.Temperature?.ToString() ?? string.Empty;
        TopP = options.TopP?.ToString() ?? string.Empty;
        MaxCompletionTokens = options.MaxCompletionTokens?.ToString() ?? string.Empty;
        MaxTokens = options.MaxTokens?.ToString() ?? string.Empty;
        FrequencyPenalty = options.FrequencyPenalty?.ToString() ?? string.Empty;
        PresencePenalty = options.PresencePenalty?.ToString() ?? string.Empty;
        RepetitionPenalty = options.RepetitionPenalty?.ToString() ?? string.Empty;
        Seed = options.Seed?.ToString() ?? string.Empty;
        NChoices = options.N?.ToString() ?? string.Empty;
        StopSequence = options.Stop ?? string.Empty;
        ResponseFormat = options.ResponseFormat ?? string.Empty;
        AdvancedOverridesJson = options.AdvancedOverridesJson ?? string.Empty;
    }

    private void RefreshPrompts()
    {
        var selectedId = SelectedPrompt?.Prompt.Id ?? _settings.Current.PostProcessing.SelectedPromptId;

        PromptOptions = new ObservableCollection<PromptOption>(
            _prompts.GetAll().Select(prompt => new PromptOption(prompt)));

        SelectedPrompt = PromptOptions.FirstOrDefault(option => option.Prompt.Id == selectedId)
            ?? PromptOptions.FirstOrDefault();
    }

    private void RefreshSpeechCommands()
    {
        BuiltInCommands = new ObservableCollection<SpeechCommand>(_speechCommands.GetBuiltIns());
        CustomCommands = new ObservableCollection<SpeechCommand>(_speechCommands.GetCustom());
    }

    private bool TryReadGenerationOptions(out TextGenerationOptions options, out string? error)
    {
        options = new TextGenerationOptions
        {
            ThinkingEnabled = ThinkingMode switch
            {
                "Enabled" => true,
                "Disabled" => false,
                _ => null,
            },
            ReasoningEffort = ReasoningEffort == "Not set" ? null : ReasoningEffort,
            Stop = string.IsNullOrWhiteSpace(StopSequence) ? null : StopSequence.Trim(),
            ResponseFormat = string.IsNullOrWhiteSpace(ResponseFormat) ? null : ResponseFormat.Trim(),
            AdvancedOverridesJson = string.IsNullOrWhiteSpace(AdvancedOverridesJson) ? null : AdvancedOverridesJson.Trim(),
        };

        error = null;

        if (!TryReadDecimal(Temperature, out var temperature)
            || !TryReadDecimal(TopP, out var topP)
            || !TryReadDecimal(FrequencyPenalty, out var frequencyPenalty)
            || !TryReadDecimal(PresencePenalty, out var presencePenalty)
            || !TryReadDecimal(RepetitionPenalty, out var repetitionPenalty))
        {
            error = "Numeric overrides must be numbers or blank.";
            return false;
        }

        if (!TryReadInt32(MaxCompletionTokens, out var maxCompletionTokens)
            || !TryReadInt32(MaxTokens, out var maxTokens)
            || !TryReadInt32(Seed, out var seed)
            || !TryReadInt32(NChoices, out var nChoices))
        {
            error = "Whole-number overrides must be integers or blank.";
            return false;
        }

        options.Temperature = temperature;
        options.TopP = topP;
        options.FrequencyPenalty = frequencyPenalty;
        options.PresencePenalty = presencePenalty;
        options.RepetitionPenalty = repetitionPenalty;
        options.MaxCompletionTokens = maxCompletionTokens;
        options.MaxTokens = maxTokens;
        options.Seed = seed;
        options.N = nChoices;

        return true;
    }

    private static bool TryReadDecimal(string value, out double? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Set(parsed, out result)
            : false;
    }

    private static bool TryReadInt32(string value, out int? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Set(parsed, out result)
            : false;
    }

    private static bool Set<T>(T value, out T? result)
    {
        result = value;
        return true;
    }

    public sealed record PromptOption(TranscriptionPrompt Prompt)
    {
        public override string ToString()
        {
            return Prompt.IsBuiltIn ? $"{Prompt.Title} (built-in)" : Prompt.Title;
        }
    }
}