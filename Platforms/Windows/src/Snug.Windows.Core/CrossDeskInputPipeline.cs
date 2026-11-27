namespace Snug.Windows.Core;

public sealed record CrossDeskCommandBatch(PointerKind Kind, IReadOnlyList<TouchCommand> Commands);
public sealed record CrossDeskInputResult(bool PassOriginal, IReadOnlyList<CrossDeskCommandBatch> Batches);

/// <summary>Translate provider-normalized finger gestures using the existing
/// scrolling and replay core. Native UI and relative cursor events pass unchanged.</summary>
public sealed class CrossDeskInputPipeline
{
    private readonly CrossDeskTouch _normalizer = new();
    private readonly TouchTranslator _translator = new();
    private bool _accepted;
    public bool NeedsTick => _normalizer.Deadline is not null || _translator.IsCoasting;

    public CrossDeskInputResult Take(CrossDeskPointerEvent ev, Func<PointD, bool> accept, double scale)
    {
        var batches = new List<CrossDeskCommandBatch>();
        if (_normalizer.Deadline is { } due && due <= ev.Time)
            Feed(_normalizer.Tick(ev.Time), accept, scale, batches);
        var result = _normalizer.Normalize(ev);
        Feed(result.Before, accept, scale, batches);
        var pass = result.Current is not { } current || Translate(current, accept, scale, batches, false);
        // Move-only absolute slides have no button to swallow; retain cursor motion.
        return new(ev.Kind == CrossDeskPointerEventKind.Move || pass, batches);
    }

    public CrossDeskInputResult Tick(double time, Func<PointD, bool> accept, double scale)
    {
        var batches = new List<CrossDeskCommandBatch>();
        if (_normalizer.Deadline is { } due && due <= time)
            Feed(_normalizer.Tick(time), accept, scale, batches);
        if (_translator.MomentumFrame() is { } step)
            batches.Add(new(PointerKind.Move, [new(TouchCommandKind.Scroll, step.Location, step)]));
        return new(true, batches);
    }

    private void Feed(IReadOnlyList<CrossDeskTouchInput> inputs, Func<PointD, bool> accept,
        double scale, List<CrossDeskCommandBatch> batches)
    {
        foreach (var input in inputs) Translate(input, accept, scale, batches, true);
    }

    private bool Translate(CrossDeskTouchInput input, Func<PointD, bool> accept, double scale,
        List<CrossDeskCommandBatch> batches, bool synthesized)
    {
        if (input.Kind == CrossDeskTouchInputKind.Press) _accepted = accept(input.Location);
        var kind = input.Kind switch
        {
            CrossDeskTouchInputKind.Press => PointerKind.Press,
            CrossDeskTouchInputKind.Release => PointerKind.Release,
            _ => PointerKind.Move
        };
        var response = input.Kind == CrossDeskTouchInputKind.Recognized
            ? _accepted && input.Gesture is { } gesture
                ? _translator.RecognizeCrossDesk(gesture, input.Time) : TouchResponse.Pass
            : _translator.Handle(new(kind, input.Location, input.Time), _accepted, scale);
        var commands = synthesized
            ? response.Commands.Where(c => c.Kind == TouchCommandKind.Scroll).ToArray()
            : response.Commands;
        if (commands.Count > 0) batches.Add(new(kind, commands));
        if (input.Kind == CrossDeskTouchInputKind.Release) _accepted = false;
        return response.PassOriginal;
    }
}
