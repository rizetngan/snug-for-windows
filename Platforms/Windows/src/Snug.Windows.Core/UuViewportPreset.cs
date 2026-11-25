namespace Snug.Windows.Core;

/// <summary>
/// UU iOS UI chrome is not a fixed device property: classic toolbar insets,
/// adaptive safe-area layout, and immersive display use different viewports.
/// Never silently change an active Windows VDD mode table.
/// </summary>
public static class UuViewportPreset
{
    public const string Classic = "Classic";
    public const string Expanded = "Expanded";
    public const string Full = "Full";

    public static string Normalize(string? value) => value switch
    {
        Expanded => Expanded,
        Full => Full,
        _ => Classic
    };

    // Logical iOS points removed from each vertical edge.
    // Classic is the original upstream ScreenFit assumption.
    // Expanded and Full are explicit user-selected calibration candidates.
    public static int VerticalInsetPoints(string? value) => Normalize(value) switch
    {
        Expanded => 45,
        Full => 0,
        _ => 91
    };
}
