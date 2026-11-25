namespace Snug.Windows.Core;

/// <summary>
/// 将纵向触控位置映射为离散缩放档位。顶部代表放大，底部代表缩小。
/// </summary>
public static class ZoomRailScale
{
    public static int ValueAt(
        double position,
        double trackTop,
        double trackHeight,
        int minimum,
        int maximum)
    {
        if (!double.IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!double.IsFinite(trackTop))
            throw new ArgumentOutOfRangeException(nameof(trackTop));
        if (!double.IsFinite(trackHeight) || trackHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(trackHeight));
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum));

        var progress = Math.Clamp((position - trackTop) / trackHeight, 0, 1);
        var distance = (int)Math.Round(
            progress * (maximum - minimum),
            MidpointRounding.AwayFromZero);
        return maximum - distance;
    }
}
