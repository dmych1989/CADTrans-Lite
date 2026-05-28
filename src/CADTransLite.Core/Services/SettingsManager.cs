// Services/SettingsManager.cs
// Loads and saves UserSettings to %APPDATA%/CADTransLite/settings.json.
// Uses System.Text.Json with camelCase naming policy.

using System.Text.Json;
using System.Text.Json.Serialization;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// Manages persistence of <see cref="UserSettings"/> to a JSON file
/// in the user's %APPDATA% directory.
/// </summary>
public sealed class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CADTransLite");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Loads settings from the persisted JSON file.
    /// Returns a new <see cref="UserSettings"/> instance with defaults if the file
    /// does not exist or cannot be parsed.
    /// </summary>
    /// <returns>The loaded <see cref="UserSettings"/>, or a default instance on failure.</returns>
    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new UserSettings();

            string json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            return settings ?? new UserSettings();
        }
        catch
        {
            // If deserialization fails for any reason, return defaults.
            return new UserSettings();
        }
    }

    /// <summary>
    /// Saves the given <see cref="UserSettings"/> to the JSON file.
    /// Creates the settings directory if it does not exist.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            if (!Directory.Exists(SettingsDirectory))
                Directory.CreateDirectory(SettingsDirectory);

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Silently ignore save errors — settings are a convenience, not critical.
        }
    }
}
