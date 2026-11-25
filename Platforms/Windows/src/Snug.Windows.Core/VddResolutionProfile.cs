using System.Xml.Linq;

namespace Snug.Windows.Core;

public sealed record VddResolutionMode(int Width, int Height, int RefreshRate);

/// <summary>
/// Generates the VDD mode list from the same phone geometry used by Snug's ScreenFit.
/// The returned order is intentional: VDD uses the first mode as its preferred mode.
/// </summary>
public static class VddResolutionProfile
{
    public static IReadOnlyList<VddResolutionMode> Create(
        PhoneChoice choice,
        int refreshRate = 60,
        int verticalInsetPoints = (int)ScreenFit.UuInset,
        int preferredBaseWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (refreshRate is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(refreshRate));

        var fit = new ScreenFit(choice.Screen, verticalInsetPoints, preferredBaseWidth);
        // Hardware-tested on this Windows 10 / signed MttVDD: 2, 5, 12 and 28
        // mode tables enumerate and switch successfully. The original 55-mode
        // table does not enumerate a monitor. Retain the upstream ScreenFit
        // algorithm, but advertise its widths every 20 points on Windows.
        // This bounds extra downscaling to <4%, versus <2% upstream.
        return SupportedWidths(choice, verticalInsetPoints, preferredBaseWidth)
            .Select(width => fit.PixelSizeFor(width))
            .Select(size => new VddResolutionMode(
                checked((int)Math.Round(size.Width, MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(size.Height, MidpointRounding.AwayFromZero)),
                refreshRate))
            .ToArray();
    }

    public static IReadOnlyList<int> SupportedWidths(PhoneChoice choice, int verticalInsetPoints = (int)ScreenFit.UuInset, int preferredBaseWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var fit = new ScreenFit(choice.Screen, verticalInsetPoints, preferredBaseWidth);
        return fit.Widths.Where((_, index) => index % 2 == 0)
            .Append(fit.Widths[^1])
            .Distinct()
            .ToArray();
    }

    public static int SnapWidth(PhoneChoice choice, int requestedWidth, int verticalInsetPoints = (int)ScreenFit.UuInset, int preferredBaseWidth = 0)
    {
        var supported = SupportedWidths(choice, verticalInsetPoints, preferredBaseWidth);
        return supported.FirstOrDefault(width => width >= requestedWidth, supported[^1]);
    }

    public static string Apply(
        string settingsXml,
        PhoneChoice choice,
        int refreshRate = 60,
        int verticalInsetPoints = (int)ScreenFit.UuInset,
        int preferredBaseWidth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsXml);
        var document = XDocument.Parse(settingsXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException("VDD 配置缺少根节点。");

        var resolutions = root.Element("resolutions");
        if (resolutions is null)
        {
            resolutions = new XElement("resolutions");
            var global = root.Element("global");
            if (global is null) root.Add(resolutions);
            else global.AddAfterSelf(resolutions);
        }

        resolutions.ReplaceNodes(Create(choice, refreshRate, verticalInsetPoints, preferredBaseWidth).Select(mode =>
            new XElement("resolution",
                new XElement("width", mode.Width),
                new XElement("height", mode.Height),
                new XElement("refresh_rate", mode.RefreshRate))));

        return document.ToString(SaveOptions.None);
    }
}
