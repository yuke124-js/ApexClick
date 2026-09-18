using ApexClick.Models;

namespace ApexClick.Services.Community;

public static class CommunitySafety
{
    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "productivity",
            "software-testing",
            "accessibility",
            "education-demo"
        };

    public static void ValidateForPublication(MacroScript script, string category)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (!AllowedCategories.Contains(category))
            throw new InvalidOperationException($"Category '{category}' is not allowed.");

        foreach (var evt in script.Events)
        {
            if (evt.Payload is not string payload)
                continue;

            if (payload.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                payload.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                payload.Contains("shell", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Scenario contains a potentially unsafe arbitrary-command payload.");
            }
        }
    }
}
