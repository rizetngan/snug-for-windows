namespace Snug.Windows.Core;

/// <summary>User-selected initial logical width, leaving the aspect ratio intact.</summary>
public static class PhoneBaseWidth
{
    private static readonly int[] Presets = [0, 600, 720, 840];
    public static IReadOnlyList<int> Options => Presets;
    public static int Normalize(int width) => Presets.Contains(width) ? width : 0;
    public static int AtIndex(int index) => Presets[Math.Clamp(index, 0, Presets.Length - 1)];
    public static int IndexOf(int width) => Array.IndexOf(Presets, Normalize(width));
}
