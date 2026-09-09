namespace ApexClick.Services.Licensing;

public sealed class FreeFeatureFlags : IFeatureFlags
{
    public LicenseTier Tier => LicenseTier.Free;
    public int? MaxSavedScenarios => 5;
    public TimeSpan? MaxRecordingDuration => TimeSpan.FromMinutes(10);
    public bool CanExportStandaloneExe => false;
    public bool HasConditionalLogic => false;
    public bool HasOcrTriggers => false;
    public bool HasPixelTriggers => false;
    public bool CanCaptureSpecificWindow => false;
    public bool HasReturnQueue => false;
    public bool HasAiGenerator => false;
    public bool HasHybridPositioning => false;
    public bool CanRebindHotkeys => false;
    public bool CanCompileToExe => false;
    public bool SmartPauseConfigurable => false;
}

public sealed class ProFeatureFlags : IFeatureFlags
{
    public LicenseTier Tier => LicenseTier.Pro;
    public int? MaxSavedScenarios => null;
    public TimeSpan? MaxRecordingDuration => null;
    public bool CanExportStandaloneExe => true;
    public bool HasConditionalLogic => true;
    public bool HasOcrTriggers => true;
    public bool HasPixelTriggers => true;
    public bool CanCaptureSpecificWindow => true;
    public bool HasReturnQueue => true;
    public bool HasAiGenerator => true;
    public bool HasHybridPositioning => true;
    public bool CanRebindHotkeys => true;
    public bool CanCompileToExe => true;
    public bool SmartPauseConfigurable => true;
}

public static class FeatureFlagsResolver
{
    public static IFeatureFlags Resolve(LicenseTier tier) => tier switch
    {
        LicenseTier.Free => new FreeFeatureFlags(),
        LicenseTier.Pro => new ProFeatureFlags(),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
