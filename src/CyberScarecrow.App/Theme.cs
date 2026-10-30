using System.Drawing.Drawing2D;

namespace CyberScarecrow;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(244, 245, 247);
    public static readonly Color Header = Color.FromArgb(18, 20, 24);
    public static readonly Color Card = Color.White;
    public static readonly Color Accent = Color.FromArgb(245, 158, 11);
    public static readonly Color AccentDark = Color.FromArgb(217, 119, 6);
    public static readonly Color Text = Color.FromArgb(26, 28, 32);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Border = Color.FromArgb(229, 231, 235);
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color Blue = Color.FromArgb(37, 99, 235);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.Card;
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        var oldRegion = Region;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 14);
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, 14);
        using var border = new Pen(Theme.Border);
        e.Graphics.DrawPath(border, path);

        base.OnPaint(e);
    }
}

internal sealed class PillLabel : Control
{
    private Color _fillColor = Theme.Muted;

    public Color FillColor
    {
        get => _fillColor;
        set
        {
            if (_fillColor == value) return;
            _fillColor = value;
            Invalidate();
        }
    }

    public Color TextColor { get; set; } = Color.White;

    public PillLabel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        Size = new Size(118, 30);
        TabStop = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Height / 2);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            TextColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
    }
}

internal sealed class ToggleSwitch : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 15 };
    private bool _checked;
    private float _position;
    private bool _hover;
    private bool _pressed;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _animationTimer.Start();
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Size = new Size(66, 36);
        Cursor = Cursors.Hand;
        TabStop = true;
        _animationTimer.Tick += (_, _) => Animate();
    }

    private void Animate()
    {
        var target = _checked ? 1f : 0f;
        var delta = target - _position;
        if (Math.Abs(delta) < 0.03f)
        {
            _position = target;
            _animationTimer.Stop();
        }
        else
        {
            _position += delta * 0.35f;
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _pressed)
        {
            _pressed = false;
            Checked = !Checked;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = _checked ? Theme.Accent : Color.FromArgb(203, 207, 214);
        if (!Enabled) track = Color.FromArgb(225, 227, 231);
        if (_hover && Enabled) track = _checked ? Theme.AccentDark : Color.FromArgb(185, 190, 199);
        if (_pressed && Enabled) track = _checked ? Color.FromArgb(180, 83, 9) : Color.FromArgb(156, 163, 175);

        var trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var trackPath = Theme.RoundedRect(trackRect, Height / 2);
        using var trackBrush = new SolidBrush(track);
        e.Graphics.FillPath(trackBrush, trackPath);

        var knobSize = Height - 8;
        var travel = Width - knobSize - 8;
        var knobX = 4 + travel * _position;
        var knobRect = new Rectangle((int)Math.Round(knobX), 4, knobSize, knobSize);
        using var knobBrush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(245, 245, 245));
        e.Graphics.FillEllipse(knobBrush, knobRect);

        if (Focused)
        {
            using var focusPen = new Pen(Theme.AccentDark, 1.5f) { DashStyle = DashStyle.Dot };
            var focusRect = new Rectangle(2, 2, Width - 5, Height - 5);
            using var focusPath = Theme.RoundedRect(focusRect, Height / 2);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animationTimer.Dispose();
        base.Dispose(disposing);
    }
}
