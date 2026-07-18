using System.Globalization;

namespace War3Rts;

/// <summary>
/// Converts original UnitFunc Animprops tokens into ordered MDX sequence
/// candidates. It knows no unit/building rawcodes: variants sharing a model
/// are selected entirely by exported configuration.
/// </summary>
public static class War3AnimationPropertyResolver
{
    public static string[] Stand(
        IReadOnlyList<string> properties,
        bool working = false)
    {
        var suffix = Suffix(properties);
        if (suffix.Length == 0)
            return working
                ? ["Stand Work", "Stand"]
                : ["Stand"];
        return working
            ? [$"Stand Work {suffix}", $"Stand {suffix}", "Stand Work", "Stand"]
            : [$"Stand {suffix}", "Stand"];
    }

    public static string[] Portrait(IReadOnlyList<string> properties)
    {
        var suffix = Suffix(properties);
        return suffix.Length == 0
            ? ["Portrait", "Stand Work", "Stand"]
            : [$"Portrait {suffix}", "Portrait", $"Stand {suffix}", "Stand"];
    }

    public static string[] UpgradeBirth(IReadOnlyList<string> properties)
    {
        var suffix = Suffix(properties);
        return suffix.Length == 0
            ? ["Birth", "Stand"]
            : [$"Birth {suffix}", "Birth Upgrade", "Birth", $"Stand {suffix}",
                "Stand"];
    }

    public static string[] Attack(IReadOnlyList<string> properties)
    {
        var suffix = Suffix(properties);
        if (suffix.Length == 0)
            return ["Attack", "Stand Ready Attack", "Stand"];
        return
        [
            $"Attack Stand Ready {suffix}",
            $"Stand {suffix} Ready Attack",
            $"Stand {suffix} Attack Ready",
            $"Attack {suffix}",
            $"Stand {suffix}",
            "Attack",
            "Stand"
        ];
    }

    private static string Suffix(IReadOnlyList<string> properties) =>
        string.Join(' ', properties
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                value.Trim().ToLowerInvariant())));
}
