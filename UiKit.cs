using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Witcher_3_Dynamic_Map;

/// <summary>Colors, fonts and small helpers of the Windows 11 style dark theme.</summary>
public static class Theme
{
    public static readonly Color Window = Color.FromArgb(31, 31, 31);
    public static readonly Color Card = Color.FromArgb(43, 43, 43);
    public static readonly Color CardBorder = Color.FromArgb(58, 58, 58);
    public static readonly Color Hover = Color.FromArgb(55, 55, 55);
    public static readonly Color Text = Color.FromArgb(255, 255, 255);
    public static readonly Color TextDim = Color.FromArgb(157, 157, 157);
    public static readonly Color Accent = Color.FromArgb(96, 205, 255);
    public static readonly Color Good = Color.FromArgb(108, 203, 95);
    public static readonly Color Warn = Color.FromArgb(252, 225, 0);
    public static readonly Color Bad = Color.FromArgb(255, 153, 164);

    private static readonly string Family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text")
        ? "Segoe UI Variable Text" : "Segoe UI";

    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new(Family, size, style);
    public static Font Mono(float size) => new("Consolas", size);

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Rounds the corners of a control (used for flat buttons).</summary>
    public static void Round(Control c, int radius)
    {
        void Apply()
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            using var path = RoundedRect(new Rectangle(0, 0, c.Width, c.Height), radius);
            c.Region = new Region(path);
        }
        c.SizeChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>Adds controls to a parent so that they appear top to bottom in the given order.</summary>
    public static void Stack(Control parent, params Control[] topToBottom)
    {
        foreach (var c in topToBottom.Reverse())
        {
            if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top;
            parent.Controls.Add(c);
        }
    }

    // Texts live in Loc (English / Russian).
    public static string RegionTitle(string region) => Loc.RegionTitle(region);
    public static string PoiTitle(string type) => Loc.PoiTitle(type);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    /// <summary>Dark title bar and caption in the window color (Windows 10 20H1+ / Windows 11).</summary>
    public static void StyleWindow(IntPtr hwnd)
    {
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4);                    // immersive dark mode
            int caption = Window.R | (Window.G << 8) | (Window.B << 16);
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);                 // caption color
            int text = 0xFFFFFF;
            DwmSetWindowAttribute(hwnd, 36, ref text, 4);                    // caption text color
        }
        catch { }
    }

    public static void DarkScrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
    }
}

/// <summary>A rounded panel with a subtle border, like the cards of Windows 11 Settings.</summary>
public class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Window; // the color outside the rounded corners
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using var fill = new SolidBrush(Theme.Card);
        using var pen = new Pen(Theme.CardBorder);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    // Labels and plain panels inside a card sit on the card color, not on the window color.
    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (e.Control is Label or Panel) e.Control.BackColor = Theme.Card;
        base.OnControlAdded(e);
    }
}

/// <summary>Colored dot with text, used for the connection status.</summary>
public sealed class StatusLabel : Control
{
    private string text = "";
    private Color color = Theme.TextDim;

    public StatusLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Card;
        Height = 22;
    }

    public void Set(string newText, Color newColor)
    {
        if (text == newText && color == newColor) return;
        text = newText; color = newColor;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, 0, (Height - 8) / 2, 8, 8);
        using var font = Theme.Font(9);
        using var brush = new SolidBrush(Theme.TextDim);
        var rect = new Rectangle(16, 0, Width - 16, Height);
        TextRenderer.DrawText(g, text, font, rect, Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Windows 11 style switch with a caption on the left.</summary>
public sealed class ToggleSwitch : Control
{
    private bool isChecked;
    private bool hover;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                 ControlStyles.StandardClick | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        Height = 36;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        const int w = 40, h = 20;
        var track = new Rectangle(Width - w - 1, (Height - h) / 2, w, h);
        using (var path = Theme.RoundedRect(track, h / 2))
        {
            if (isChecked)
            {
                using var fill = new SolidBrush(hover ? Color.FromArgb(120, 214, 255) : Theme.Accent);
                g.FillPath(fill, path);
            }
            else
            {
                using var pen = new Pen(hover ? Color.FromArgb(200, 200, 200) : Color.FromArgb(160, 160, 160), 1.5f);
                g.DrawPath(pen, path);
            }
        }

        int knob = isChecked ? 14 : 12;
        int kx = isChecked ? track.Right - knob - 4 : track.X + 4;
        using var knobBrush = new SolidBrush(isChecked ? Color.Black : Color.FromArgb(200, 200, 200));
        g.FillEllipse(knobBrush, kx, track.Y + (h - knob) / 2, knob, knob);

        using var font = Theme.Font(9.5f);
        TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, track.X - 8, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Two-segment pill ("EN | RU") for choosing the language.</summary>
public sealed class LanguageSwitch : Control
{
    private static readonly string[] Codes = { Loc.English, Loc.Russian };
    private static readonly string[] Labels = { "EN", "RU" };

    public event EventHandler? SelectionChanged;

    public LanguageSwitch()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(76, 26);
        Cursor = Cursors.Hand;
        BackColor = Theme.Card;
    }

    /// <summary>Selected language code ("en" or "ru").</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Code { get; set; } = Loc.English;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var code = Codes[e.X < Width / 2 ? 0 : 1];
        if (code == Code) return;
        Code = code;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var track = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var fill = new SolidBrush(Theme.Hover))
            g.FillPath(fill, track);

        int half = Width / 2;
        using var font = Theme.Font(8.5f, FontStyle.Bold);
        for (int i = 0; i < 2; i++)
        {
            var seg = new Rectangle(i * half + 2, 2, half - 3, Height - 5);
            bool on = Codes[i] == Code;
            if (on)
            {
                using var path = Theme.RoundedRect(seg, seg.Height / 2);
                using var accent = new SolidBrush(Theme.Accent);
                g.FillPath(accent, path);
            }
            TextRenderer.DrawText(g, Labels[i], font, seg, on ? Color.Black : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Small round button with a chevron that hides and shows the side panel.</summary>
public sealed class PanelToggleButton : Control
{
    private bool hover;
    private bool collapsed;

    public PanelToggleButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(34, 34);
        Cursor = Cursors.Hand;
        BackColor = Theme.Card;
        Theme.Round(this, 9);
    }

    /// <summary>True while the panel is hidden (the chevron then points to the right).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Collapsed
    {
        get => collapsed;
        set { collapsed = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(hover ? Theme.Hover : Theme.Card)) g.FillRectangle(bg, ClientRectangle);
        using (var border = new Pen(Theme.CardBorder))
        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 9))
            g.DrawPath(border, path);

        float cx = Width / 2f, cy = Height / 2f, dir = collapsed ? 1 : -1; // -1: points left (hide)
        using var pen = new Pen(Theme.Text, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(cx - dir * 3, cy - 6),
            new PointF(cx + dir * 3, cy),
            new PointF(cx - dir * 3, cy + 6),
        });
    }
}

/// <summary>Flat accent button with rounded corners.</summary>
public sealed class AccentButton : Button
{
    public AccentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Accent;
        ForeColor = Color.Black;
        Font = Theme.Font(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(120, 214, 255);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(80, 180, 230);
        Theme.Round(this, 6);
    }
}

/// <summary>Checkbox list of POI types with the same icons that are drawn on the map.</summary>
public sealed class FilterList : ListBox
{
    private readonly Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> off = new(StringComparer.OrdinalIgnoreCase);
    private int hoverIndex = -1;

    /// <summary>Returns the icon of a POI type (shared with the map).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, Bitmap?>? IconProvider { get; set; }
    public event EventHandler? FilterChanged;

    /// <summary>Types switched off by the user; kept when the list content changes (other region, new data).</summary>
    public IReadOnlyCollection<string> Disabled => off;

    public FilterList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        SelectionMode = SelectionMode.None;
        ItemHeight = 36;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkScrollbars(Handle);
    }

    public void SetItems(IEnumerable<(string Type, int Count)> items)
    {
        var list = items.OrderBy(i => Theme.PoiTitle(i.Type), StringComparer.CurrentCultureIgnoreCase).ToList();

        // The game sends a snapshot every second; rebuilding an unchanged list would flicker and reset the scroll.
        bool sameTypes = list.Count == Items.Count && list.Select(i => i.Type).SequenceEqual(Items.Cast<string>());
        bool sameCounts = sameTypes && list.All(i => counts.TryGetValue(i.Type, out var c) && c == i.Count);
        if (sameCounts) return;

        counts.Clear();
        foreach (var (type, count) in list) counts[type] = count;

        if (sameTypes) { Invalidate(); return; }

        int top = TopIndex;
        BeginUpdate();
        Items.Clear();
        foreach (var (type, _) in list) Items.Add(type);
        if (top < Items.Count) TopIndex = top;
        EndUpdate();
    }

    private void InvalidateItem(int index)
    {
        if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
    }

    public void SetAll(bool enabled)
    {
        if (enabled) off.Clear();
        else foreach (var t in Items.Cast<string>()) off.Add(t);
        Invalidate();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = IndexFromPoint(e.Location);
        if (i < 0 || e.Button != MouseButtons.Left) return;
        var type = (string)Items[i];
        if (!off.Remove(type)) off.Add(type);
        InvalidateItem(i);
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    // Only the rows that change are repainted: the row that lost the hover and the one that got it.
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexFromPoint(e.Location);
        if (i == hoverIndex) return;
        int old = hoverIndex;
        hoverIndex = i;
        InvalidateItem(old);
        InvalidateItem(i);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        int old = hoverIndex;
        hoverIndex = -1;
        InvalidateItem(old);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var type = (string)Items[e.Index];
        bool on = !off.Contains(type);

        // The row is drawn off-screen and copied in one go, so repainting it does not flicker.
        var r = new Rectangle(0, 0, e.Bounds.Width, e.Bounds.Height);
        using var buffer = new Bitmap(r.Width, r.Height);
        using var g = Graphics.FromImage(buffer);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(e.Index == hoverIndex ? Theme.Hover : Theme.Card))
            g.FillRectangle(bg, r);

        // checkbox
        var box = new Rectangle(r.X + 8, r.Y + (r.Height - 18) / 2, 18, 18);
        using (var path = Theme.RoundedRect(box, 4))
        {
            if (on)
            {
                using var fill = new SolidBrush(Theme.Accent);
                g.FillPath(fill, path);
                using var tick = new Pen(Color.Black, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(box.X + 4.5f, box.Y + 9.5f),
                    new PointF(box.X + 7.5f, box.Y + 12.5f),
                    new PointF(box.X + 13.5f, box.Y + 5.5f),
                });
            }
            else
            {
                using var pen = new Pen(Color.FromArgb(160, 160, 160), 1.5f);
                g.DrawPath(pen, path);
            }
        }

        // map icon (dimmed when the type is switched off)
        var icon = IconProvider?.Invoke(type);
        var iconRect = new Rectangle(box.Right + 10, r.Y + (r.Height - 22) / 2, 22, 22);
        if (icon is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (on) g.DrawImage(icon, iconRect);
            else
            {
                using var dim = new System.Drawing.Imaging.ImageAttributes();
                dim.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
                g.DrawImage(icon, iconRect, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, dim);
            }
        }

        using var font = Theme.Font(9.5f);
        var textColor = on ? Theme.Text : Theme.TextDim;
        var countText = counts.TryGetValue(type, out var c) ? c.ToString() : "";
        int countW = TextRenderer.MeasureText(g, countText, font).Width + 8;

        TextRenderer.DrawText(g, Theme.PoiTitle(type), font,
            new Rectangle(iconRect.Right + 8, r.Y, r.Width - iconRect.Right - 8 - countW - 6, r.Height), textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, countText, font,
            new Rectangle(r.Right - countW - 6, r.Y, countW, r.Height), Theme.TextDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        e.Graphics.DrawImageUnscaled(buffer, e.Bounds.Location);
    }
}
