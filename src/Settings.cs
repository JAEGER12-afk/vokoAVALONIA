using System.Text.Json;

namespace VokoAvalonia;

/// <summary>
/// Everything the user can change, with its default value.
/// Saved as JSON in %AppData%\VokoAvalonia\settings.json (Windows)
/// or ~/.config/VokoAvalonia/settings.json (Linux).
///
/// To add a new setting: add a property here with a default value.
/// Loading and saving pick it up automatically.
/// </summary>
public class Settings
{
    // --- Screen ---
    public int ScreenIndex { get; set; } = 0;          // 0 = first display in the list
    public bool RecordCursor { get; set; } = true;
    public bool MinimizeOnStart { get; set; } = true;

    // --- Audio ---
    public bool RecordMicrophone { get; set; } = false;
    public string Microphone { get; set; } = "";       // device name as ffmpeg reports it

    // --- Video ---
    public string Format { get; set; } = "mkv";        // "mkv" or "mp4"
    public int Fps { get; set; } = 30;
    public int Quality { get; set; } = 23;             // x264 CRF: lower = better quality, bigger file

    // --- Storage ---
    public string OutputFolder { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    // --- Shortcuts (always Ctrl + Alt + this letter) ---
    public string RecordKey { get; set; } = "R";       // start, then pause / continue
    public string StopKey { get; set; } = "S";
    public string SnapshotKey { get; set; } = "C";

    // --- Look ---
    public string Theme { get; set; } = "System";      // "System", "Light" or "Dark"


    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VokoAvalonia", "settings.json");

    /// <summary>Reads the settings file. Returns defaults if it is missing or broken.</summary>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception)
        {
            // A broken settings file should never stop the app from starting.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, options));
    }
}
