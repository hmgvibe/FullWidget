namespace FullWidget.Windows;

/// Where a dragged widget lands, following Secondary's rules: within a few pixels the card snaps
/// to the nearest line — the screen's edges and centre, other widgets' edges and centres — one
/// line per direction, and a guide shows what it lined up with.
///
/// Each card keeps half the gap around it, so cards placed side by side end up one gap apart and
/// one gap from the screen edge, while edges and centres still line up exactly.
static class SnapLayout
{
    /// A guide line in physical pixels: vertical lines sit at x = At and run from y = From to To.
    public readonly record struct Guide(bool Vertical, int At, int From, int To);

    [Flags]
    public enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    /// card, work and peers in physical pixels. resizing: the edges being dragged (none = moving).
    /// min and max limit the card's size while resizing.
    public static (Rectangle Card, List<Guide> Guides) Snap(Rectangle card, Rectangle work, IReadOnlyList<Rectangle> peers,
        int gap, int reach, Edges resizing = Edges.None, Size min = default, Size max = default)
    {
        int half = gap / 2;
        bool moving = resizing == Edges.None;
        var guides = new List<Guide>();

        var x = Nearest(Span.X(card), Span.X(work), peers.Select(Span.X).ToList(), half, reach, moving,
            resizing.HasFlag(Edges.Left), resizing.HasFlag(Edges.Right), min.Width, max.Width);
        var y = Nearest(Span.Y(card), Span.Y(work), peers.Select(Span.Y).ToList(), half, reach, moving,
            resizing.HasFlag(Edges.Top), resizing.HasFlag(Edges.Bottom), min.Height, max.Height);

        int left = card.Left, right = card.Right, top = card.Top, bottom = card.Bottom;
        if (x is { } sx) Apply(ref left, ref right, sx, moving);
        if (y is { } sy) Apply(ref top, ref bottom, sy, moving);
        var result = Rectangle.FromLTRB(left, top, right, bottom);

        // Each guide runs across the card and everything it lined up with.
        if (x is { } gx) { var (from, to) = Extent(gx, Span.Y(result), Span.Y(work), peers, Span.X, Span.Y, half); guides.Add(new Guide(true, gx.Line, from, to)); }
        if (y is { } gy) { var (from, to) = Extent(gy, Span.X(result), Span.X(work), peers, Span.Y, Span.X, half); guides.Add(new Guide(false, gy.Line, from, to)); }
        return (result, guides);
    }

    /// One axis of a rectangle.
    readonly record struct Span(int Lo, int Hi)
    {
        public int Mid => (Lo + Hi) / 2;
        public static Span X(Rectangle r) => new(r.Left, r.Right);
        public static Span Y(Rectangle r) => new(r.Top, r.Bottom);
    }

    enum Part { Start, Middle, End }

    /// A snap on one axis: move the card's part by Shift, which puts it on Line.
    readonly record struct Hit(Part Part, int Shift, int Line, bool Screen);

    static (int From, int To) Extent(Hit hit, Span card, Span work, IReadOnlyList<Rectangle> peers,
        Func<Rectangle, Span> along, Func<Rectangle, Span> across, int half)
    {
        if (hit.Screen) return (work.Lo, work.Hi);
        int from = card.Lo - half, to = card.Hi + half;
        foreach (var p in peers)
        {
            var a = along(p);
            bool on = hit.Part == Part.Middle ? a.Mid == hit.Line : a.Lo - half == hit.Line || a.Hi + half == hit.Line;
            if (!on) continue;
            var c = across(p);
            from = Math.Min(from, c.Lo - half);
            to = Math.Max(to, c.Hi + half);
        }
        return (from, to);
    }

    static void Apply(ref int lo, ref int hi, Hit hit, bool moving)
    {
        if (moving) { lo += hit.Shift; hi += hit.Shift; }
        else if (hit.Part == Part.Start) lo += hit.Shift;
        else hi += hit.Shift;
    }

    static Hit? Nearest(Span card, Span work, List<Span> peers, int half, int reach, bool moving,
        bool startEdge, bool endEdge, int min, int max)
    {
        Hit? best = null;
        void Consider(Part part, int source, int line, bool screen)
        {
            int shift = line - source;
            if (Math.Abs(shift) > reach) return;
            if (!moving)
            {
                // resizing: the card must stay within its size limits
                int size = part == Part.Start ? card.Hi - (card.Lo + shift) : card.Hi + shift - card.Lo;
                if (size < min || (max > 0 && size > max)) return;
            }
            var hit = new Hit(part, shift, line, screen);
            if (best is not { } b || Better(hit, b)) best = hit;
        }

        // the card's edges, half a gap out; its centre as it is
        var sources = new List<(Part part, int at)>();
        if (moving || startEdge) sources.Add((Part.Start, card.Lo - half));
        if (moving || endEdge) sources.Add((Part.End, card.Hi + half));
        if (moving) sources.Add((Part.Middle, card.Mid));

        foreach (var (part, at) in sources)
        {
            if (part == Part.Middle)
            {
                Consider(part, at, work.Mid, true);
                foreach (var p in peers) Consider(part, at, p.Mid, false);
            }
            else
            {
                Consider(part, at, work.Lo + half, true);
                Consider(part, at, work.Hi - half, true);
                foreach (var p in peers) { Consider(part, at, p.Lo - half, false); Consider(part, at, p.Hi + half, false); }
            }
        }
        return best;
    }

    /// Closest first; on a tie the screen wins, then the lower line, so the result never depends
    /// on the order widgets were listed in.
    static bool Better(Hit a, Hit b)
    {
        int da = Math.Abs(a.Shift), db = Math.Abs(b.Shift);
        if (da != db) return da < db;
        if (a.Screen != b.Screen) return a.Screen;
        if (a.Line != b.Line) return a.Line < b.Line;
        return a.Part < b.Part;
    }
}
