using System.Text.Json.Serialization;

namespace Spat.Libraries.Updater;

/// <summary>
/// Source-generated JSON so release metadata deserialization stays AOT-safe.
/// </summary>
[JsonSerializable(typeof(GitHubRelease))]
internal partial class UpdaterSerializerContext : JsonSerializerContext
{
}

