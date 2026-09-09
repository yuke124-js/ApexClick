using System.Text.Json;

namespace ApexClick.Models;

public static class PayloadConvert
{
    public static int? TryGetInt32(object? payload) => payload switch
    {
        null => null,
        int i => i,
        long l => unchecked((int)l),
        short s => s,
        JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var v) => v,
        _ => null,
    };

    public static double? TryGetDouble(object? payload) => payload switch
    {
        null => null,
        double d => d,
        int i => i,
        long l => l,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
        _ => null,
    };

    public static string? TryGetString(object? payload) => payload switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        _ => null,
    };
}
