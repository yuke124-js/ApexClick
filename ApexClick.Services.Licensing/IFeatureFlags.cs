namespace ApexClick.Services.Licensing;

public interface IFeatureFlags
{
    LicenseTier Tier { get; }

    int? MaxSavedScenarios { get; }

    TimeSpan? MaxRecordingDuration { get; }

    bool CanExportStandaloneExe { get; }
    bool HasConditionalLogic { get; }
    bool HasOcrTriggers { get; }
    bool HasPixelTriggers { get; }
    bool CanCaptureSpecificWindow { get; }
    bool HasReturnQueue { get; }
    bool HasAiGenerator { get; }
    bool HasHybridPositioning { get; }
    bool CanRebindHotkeys { get; }
    bool CanCompileToExe { get; }

    bool SmartPauseConfigurable { get; }
}
