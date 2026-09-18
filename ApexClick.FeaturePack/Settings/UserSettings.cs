using System.Text.Json;

namespace ApexClick.FeaturePack.Settings;

public sealed class UserSettings
{
    public RecordingSettings Recording { get; init; } = new();
    public PlaybackSettings Playback { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public GraphEditorSettings Graph { get; init; } = new();
    public VisionSettings Vision { get; init; } = new();
    public AiSettings AI { get; init; } = new();
    public PrivacySettings Privacy { get; init; } = new();
    public GeneralSettings General { get; init; } = new();
}

public sealed class RecordingSettings
{
    public bool CaptureMouseMoves { get; set; } = true;
    public int MouseMoveSampleIntervalMs { get; set; } = 2;
    public int MouseJitterTolerancePixels { get; set; } = 2;
    public bool AutoSimplifyGraph { get; set; } = true;
    public int MaxGraphPathPoints { get; set; } = 32;
    public bool CaptureKeyboardLayout { get; set; } = true;
    public bool CaptureTextInput { get; set; } = true;
    public bool CaptureScreenshotsOnHotkey { get; set; } = true;
    public bool RecordMicrophone { get; set; } = false;
}

public sealed class PlaybackSettings
{
    public double DefaultSpeed { get; set; } = 1.0;
    public bool ConfirmBeforeAiScenario { get; set; } = true;
    public bool EnableSmartPause { get; set; } = true;
    public int SmartPauseSeconds { get; set; } = 3;
    public bool StopOnRealInput { get; set; } = true;
    public bool ContinueOnMissingTemplate { get; set; } = false;
    public bool EnableStallWatchdog { get; set; } = false;
    public int StallWatchdogIntervalMs { get; set; } = 1000;
}

public sealed class AudioSettings
{
    public bool UseMicrophone { get; set; } = false;
    public string? MicrophoneDeviceId { get; set; }
    public bool VoiceTriggerEnabled { get; set; } = false;
    public string VoiceTriggerPhrase { get; set; } = "ApexClick";
    public string VoiceTriggerCulture { get; set; } = "ru-RU";
    public int InputVolumePercent { get; set; } = 100;
}

public sealed class GraphEditorSettings
{
    public bool ShowGrid { get; set; } = true;
    public bool ReactiveGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = false;
    public int GridSize { get; set; } = 16;
    public bool AutoArrange { get; set; } = true;
    public bool ShowPathPreview { get; set; } = true;
    public bool CompactNodes { get; set; } = false;
    public double Zoom { get; set; } = 1.0;
    public bool ConfirmDeleteNode { get; set; } = true;
}

public sealed class VisionSettings
{
    public double TemplateSimilarityThreshold { get; set; } = 0.90;
    public int PixelTolerance { get; set; } = 10;
    public bool UseDpiCompensation { get; set; } = true;
    public bool UseVirtualScreen { get; set; } = true;
    public bool PreferWindowCapture { get; set; } = false;
}

public sealed class AiSettings
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4.1-mini";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyProtected { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.1;
    public bool RequireReview { get; set; } = true;
}

public sealed class PrivacySettings
{
    public bool StoreAiPrompts { get; set; } = false;
    public bool StoreDiagnostics { get; set; } = true;
    public bool UploadCommunityTelemetry { get; set; } = false;
    public bool AllowThirdPartyPlugins { get; set; } = true;
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool RespectWindowsAnimationSetting { get; set; } = true;
}

public sealed class UserSettingsService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public UserSettings Current { get; private set; }
    public event EventHandler? Changed;

    public UserSettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexClick");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
        Normalize(Current);
    }

    public void Save(UserSettings settings)
    {
        Normalize(settings);
        settings.AI.ApiKeyProtected = SecretProtector.Protect(settings.AI.ApiKey);
        Current = settings;
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload() => Current = Load();

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserSettings();
            var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path)) ?? new UserSettings();
            loaded.AI.ApiKey = SecretProtector.Unprotect(loaded.AI.ApiKeyProtected);
            return loaded;
        }
        catch { return new UserSettings(); }
    }

    private static void Normalize(UserSettings s)
    {
        s.Recording.MouseMoveSampleIntervalMs = Math.Clamp(s.Recording.MouseMoveSampleIntervalMs, 1, 100);
        s.Recording.MouseJitterTolerancePixels = Math.Clamp(s.Recording.MouseJitterTolerancePixels, 0, 100);
        s.Recording.MaxGraphPathPoints = Math.Clamp(s.Recording.MaxGraphPathPoints, 4, 128);
        s.Playback.DefaultSpeed = Math.Clamp(s.Playback.DefaultSpeed, 0.5, 2.0);
        s.Playback.SmartPauseSeconds = Math.Clamp(s.Playback.SmartPauseSeconds, 1, 60);
        s.Playback.StallWatchdogIntervalMs = Math.Clamp(s.Playback.StallWatchdogIntervalMs, 500, 10000);
        s.Audio.InputVolumePercent = Math.Clamp(s.Audio.InputVolumePercent, 0, 100);
        s.Graph.GridSize = Math.Clamp(s.Graph.GridSize, 4, 128);
        s.Graph.Zoom = Math.Clamp(s.Graph.Zoom, 0.25, 3.0);
        s.Vision.TemplateSimilarityThreshold = Math.Clamp(s.Vision.TemplateSimilarityThreshold, 0, 1);
        s.Vision.PixelTolerance = Math.Clamp(s.Vision.PixelTolerance, 0, 255);
        s.AI.Temperature = Math.Clamp(s.AI.Temperature, 0, 2);
    }
}
