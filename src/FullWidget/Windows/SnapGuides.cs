using static FullWidget.Native.Win32;

namespace FullWidget.Windows;

/// The dashed lines shown while a widget is dragged, one per direction: what it lined up with.
/// Each line is its own small window above everything; clicks pass through it.
static class SnapGuides
{
    static GuideLine? vertical, horizontal;

    /// thickness, dash and space in physical pixels.
    public static void Show(IReadOnlyList<SnapLayout.Guide> guides, Color ink, int thickness, int dash, int space)
    {
        var v = guides.FirstOrDefault(g => g.Vertical);
        var h = guides.FirstOrDefault(g => !g.Vertical);
        Place(ref vertical, guides.Any(g => g.Vertical) ? v : null, ink, thickness, dash, space);
        Place(ref horizontal, guides.Any(g => !g.Vertical) ? h : null, ink, thickness, dash, space);
    }

    public static void Hide()
    {
        vertical?.Hide();
        horizontal?.Hide();
    }

    static void Place(ref GuideLine? line, SnapLayout.Guide? guide, Color ink, int thickness, int dash, int space)
    {
        if (guide is not { } g) { line?.Hide(); return; }
        if (line == null || line.IsDisposed) line = new GuideLine();
        var bounds = g.Vertical
            ? new Rectangle(g.At - thickness / 2, g.From, thickness, Math.Max(1, g.To - g.From))
            : new Rectangle(g.From, g.At - thickness / 2, Math.Max(1, g.To - g.From), thickness);
        bool changed = line.Ink != ink || line.Vertical != g.Vertical || line.Dash != dash || line.Space != space;
        (line.Ink, line.Vertical, line.Dash, line.Space) = (ink, g.Vertical, dash, space);
        if (!line.Visible) line.Show();
        // above every window, without taking the focus from anything
        SetWindowPos(line.Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        if (changed) line.Invalidate();
    }

    sealed class GuideLine : Form
    {
        public Color Ink;
        public bool Vertical;
        public int Dash = 6, Space = 4;

        public GuideLine()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            // the gaps between dashes show what is behind
            BackColor = TransparencyKey = Color.FromArgb(255, 0, 255);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // sizes are set in physical pixels
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var brush = new SolidBrush(Ink);
            int length = Vertical ? Height : Width;
            for (int p = 0; p < length; p += Dash + Space)
                e.Graphics.FillRectangle(brush, Vertical ? new Rectangle(0, p, Width, Dash) : new Rectangle(p, 0, Dash, Height));
        }
    }
}
