namespace Snug.Windows.Core;

public readonly record struct PhoneSize(double Width, double Height);

/// <summary>把原版 ScreenFit 的比例、档位和窗口适配算法移植到 Windows Core。</summary>
public sealed class ScreenFit
{
    public const int DesignWidth = 540;
    public const int MinWidth = 540;
    public const int MinHeight = 800;
    public const int MaxWidth = 1080;
    public const int Step = 10;
    public const double AssumedPoints = 400;
    public const double BaseHomeBarHeight = 28;
    public const double UuInset = 91;
    public const double MinRatio = 1;
    public const double MaxRatio = 2.5;

    private readonly int[] _widths;

    public double Ratio { get; }
    public int BaseWidth { get; }
    public IReadOnlyList<int> Widths => _widths;

    public static ScreenFit Standard { get; } = new(PhoneChoice.Standard.Screen);

    public ScreenFit(PhoneScreen phone, int verticalInsetPoints = (int)UuInset,
        int preferredBaseWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(phone);
        if (!phone.IsValid) throw new ArgumentException("手机屏幕参数无效。", nameof(phone));
        if (verticalInsetPoints is < 0 or > 180)
            throw new ArgumentOutOfRangeException(nameof(verticalInsetPoints));
        if (preferredBaseWidth < 0 || preferredBaseWidth > MaxWidth)
            throw new ArgumentOutOfRangeException(nameof(preferredBaseWidth));
        var points = phone.Points ?? AssumedPoints;
        Ratio = Clamp((double)phone.Height / phone.Width - 2 * verticalInsetPoints / points, MinRatio, MaxRatio);
        var minimumWidth = Enumerable.Range(0, (MaxWidth - MinWidth) / Step + 1)
            .Select(index => MinWidth + index * Step)
            .FirstOrDefault(width => Math.Round(width * Ratio, MidpointRounding.AwayFromZero) >= MinHeight, MaxWidth);
        var requested = preferredBaseWidth == 0
            ? MinWidth : (int)Math.Ceiling(preferredBaseWidth / (double)Step) * Step;
        BaseWidth = Math.Max(minimumWidth, requested);
        _widths = Enumerable.Range(0, (MaxWidth - BaseWidth) / Step + 1)
            .Select(index => BaseWidth + index * Step)
            .ToArray();
    }

    public PhoneSize SizeFor(int width)
    {
        if (width < BaseWidth || width > MaxWidth || (width - BaseWidth) % Step != 0)
            throw new ArgumentOutOfRangeException(nameof(width), "宽度必须是当前机型支持的档位。");
        return new PhoneSize(width, Math.Round(width * Ratio, MidpointRounding.AwayFromZero));
    }

    /// <summary>根据 App 实际窗口尺寸选最窄的可容纳档位；不够时使用最宽档。</summary>
    public int WidthToFit(PhoneSize window, PhoneSize area, int current)
    {
        if (area.Width <= 0 || area.Height <= 0) return current;
        const double tolerance = 2;
        if (window.Width <= area.Width + tolerance && window.Height <= area.Height + tolerance)
            return current;

        var baseWidth = current;
        var needed = Math.Max(baseWidth * window.Width / area.Width, baseWidth * window.Height / area.Height);
        return _widths.FirstOrDefault(width => width >= Math.Ceiling(needed), MaxWidth);
    }

    public static double Scale(double screenWidth) => screenWidth / DesignWidth;

    public static int HomeBarHeight(double screenWidth) =>
        (int)Math.Round(BaseHomeBarHeight * Scale(screenWidth), MidpointRounding.AwayFromZero);

    public PhoneSize PixelSizeFor(int width)
    {
        var size = SizeFor(width);
        return new PhoneSize(size.Width * 2, size.Height * 2);
    }

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}
