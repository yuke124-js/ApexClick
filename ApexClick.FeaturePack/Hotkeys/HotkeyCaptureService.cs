namespace ApexClick.FeaturePack.Hotkeys;

public sealed record CapturedHotkey(uint Modifiers,uint VirtualKey,string Display);
public sealed class HotkeyCaptureService
{
    public CapturedHotkey Capture(uint modifiers,uint virtualKey,string display)=>new(modifiers,virtualKey,display);
}
