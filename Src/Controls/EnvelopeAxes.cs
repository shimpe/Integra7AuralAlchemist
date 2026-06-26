using Avalonia;
using Avalonia.Media;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>Shared X (time) / Y (level) axis + tick drawing for envelope graphs (ticks every 16
/// of the 0..127 range). Used by both the single and dual ADSR controls so they look identical.</summary>
public static class EnvelopeAxes
{
    public const int TickStep = 16;

    public static void Draw(DrawingContext ctx, double w, double h, double sustainWidth, IPen gridPen, IPen axisPen)
    {
        for (var level = 0; level <= SnsEnvelopeMapping.Max; level += TickStep)
        {
            var y = SnsEnvelopeMapping.LevelToY(level, h);
            ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            ctx.DrawLine(axisPen, new Point(0, y), new Point(6, y));
        }

        var step = SnsEnvelopeMapping.TimeToWidth(TickStep, SnsEnvelopeMapping.SegmentMax(w, sustainWidth));
        if (step > 2)
            for (var x = step; x < w - 1; x += step)
                ctx.DrawLine(axisPen, new Point(x, h - 6), new Point(x, h));

        ctx.DrawLine(axisPen, new Point(0, 0), new Point(0, h));
        ctx.DrawLine(axisPen, new Point(0, h), new Point(w, h));
    }
}
