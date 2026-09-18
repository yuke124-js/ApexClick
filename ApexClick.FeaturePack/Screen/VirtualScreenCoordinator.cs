using System.Linq;
using System;
using ApexClick.Models;

namespace ApexClick.FeaturePack.Screen;

public sealed class VirtualScreenCoordinator
{
    public MacroScript NormalizeRecordedScript(
        MacroScript script,
        VirtualScreenBounds virtualScreen,
        int primaryWidth,
        int primaryHeight)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!virtualScreen.IsValid) throw new ArgumentOutOfRangeException(nameof(virtualScreen));

        var result = new MacroScript
        {
            Id = script.Id,
            Name = script.Name,
            CreatedAtUtc = script.CreatedAtUtc,
            LastModifiedUtc = script.LastModifiedUtc,
            PlaybackSpeedMultiplier = script.PlaybackSpeedMultiplier,
            CommunityCategory = script.CommunityCategory
        };

        foreach (var item in script.Metadata)
            result.Metadata[item.Key] = item.Value;

        result.Metadata["recording.coordinateSpace"] = "virtual-screen";
        result.Metadata["recording.virtualLeft"] = virtualScreen.Left.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata["recording.virtualTop"] = virtualScreen.Top.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata["recording.virtualWidth"] = virtualScreen.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata["recording.virtualHeight"] = virtualScreen.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var trigger in script.Triggers)
            result.Triggers.Add(trigger);
        foreach (var asset in script.EmbeddedAssets)
            result.EmbeddedAssets[asset.Key] = asset.Value;

        foreach (var evt in script.Events)
        {
            double? x = evt.NormalizedX;
            double? y = evt.NormalizedY;
            if (x is { } nx && y is { } ny)
            {
                var virtualPoint = ScreenCoordinateMath.ToVirtualNormalizedFromPrimaryNormalized(
                    nx, ny, primaryWidth, primaryHeight, virtualScreen);
                x = virtualPoint.X;
                y = virtualPoint.Y;
            }

            result.Events.Add(new MacroEvent
            {
                Id = evt.Id,
                TimestampMs = evt.TimestampMs,
                Type = evt.Type,
                NormalizedX = x,
                NormalizedY = y,
                VirtualKeyCode = evt.VirtualKeyCode,
                ScanCode = evt.ScanCode,
                KeyboardLayoutId = evt.KeyboardLayoutId,
                Payload = evt.Payload
            });
        }

        return result;
    }

    public bool RequiresVirtualDesktopPlayback(MacroScript script)
    {
        if (!string.Equals(
                script.Metadata.GetValueOrDefault("recording.coordinateSpace"),
                "virtual-screen",
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryInt(script, "recording.virtualLeft", out var left) ||
            !TryInt(script, "recording.virtualTop", out var top) ||
            !TryInt(script, "recording.virtualWidth", out var width) ||
            !TryInt(script, "recording.virtualHeight", out var height) ||
            !TryInt(script, "recording.screenWidth", out var primaryWidth) ||
            !TryInt(script, "recording.screenHeight", out var primaryHeight))
            return true;

        var primaryMinX = (0d - left) / width;
        var primaryMaxX = (primaryWidth - left) / (double)width;
        var primaryMinY = (0d - top) / height;
        var primaryMaxY = (primaryHeight - top) / (double)height;

        return script.Events.Any(e =>
            e.NormalizedX is { } x && e.NormalizedY is { } y &&
            (x < primaryMinX || x > primaryMaxX || y < primaryMinY || y > primaryMaxY));
    }

    private static bool TryInt(MacroScript script, string key, out int value)
    {
        value = 0;
        if (!script.Metadata.TryGetValue(key, out var raw))
            return false;

        return int.TryParse(
            raw,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}