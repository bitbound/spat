using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Spat.Libraries.Core.CustomDictionary;
using Spat.Libraries.Core.History;
using Spat.Libraries.Core.Prompts;
using Spat.Libraries.Core.Settings;

namespace Spat.Libraries.Core.Serialization;

public static class SpatJson
{
    /// <summary>
    /// Source-generated so settings, prompts and history survive NativeAOT trimming; reflection-based
    /// serialization would fail at runtime once the metadata is stripped.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    // Typed metadata pulled from the same options, so call sites can use the JsonTypeInfo overloads
    // and stay clean under trimming.
    public static JsonTypeInfo<AppSettings> AppSettingsInfo { get; } = (JsonTypeInfo<AppSettings>)Options.GetTypeInfo(typeof(AppSettings));

    public static JsonTypeInfo<List<TranscriptionPrompt>> PromptsInfo { get; } = (JsonTypeInfo<List<TranscriptionPrompt>>)Options.GetTypeInfo(typeof(List<TranscriptionPrompt>));

    public static JsonTypeInfo<List<CustomDictionaryEntry>> DictionaryInfo { get; } = (JsonTypeInfo<List<CustomDictionaryEntry>>)Options.GetTypeInfo(typeof(List<CustomDictionaryEntry>));

    public static JsonTypeInfo<List<HistoryEntry>> HistoryInfo { get; } = (JsonTypeInfo<List<HistoryEntry>>)Options.GetTypeInfo(typeof(List<HistoryEntry>));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(SpatSerializerContext.Default.Options);

        // The generic converter is the AOT-compatible spelling; the non-generic one needs codegen at
        // runtime. Only these two enums ever reach a file.
        options.Converters.Add(new JsonStringEnumConverter<ThemeMode>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<ShortcutTriggerMode>(JsonNamingPolicy.CamelCase));

        return options;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<TranscriptionPrompt>))]
[JsonSerializable(typeof(List<CustomDictionaryEntry>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
public sealed partial class SpatSerializerContext : JsonSerializerContext;
