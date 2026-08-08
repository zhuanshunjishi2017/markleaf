using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class OpenFolderPrompt : Control
{
    private Color _bgHover = SystemColors.HotTrack;
    private Color _bgPrimary = Color.White;
    private Color _accent = SystemColors.Highlight;
    private Color _textHover = SystemColors.ControlText;

    private bool _hovered;
    private Font _iconFont = new("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _textFont = new("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);

    private const string FolderIcon = "";
    private static string ButtonText => Loc.Get("sidebar.openFolder");

    private Rectangle _buttonBounds;

    public OpenFolderPrompt()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Height = this.ScaleForDpi(44);
        TabStop = true;
        BackColor = _bgPrimary;
    }

    public event EventHandler? FolderOpenRequested;

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("theme-dark", out c)) _bgHover = c;
        if (colors.TryGetValue("theme-light", out c)) _accent = c;
        if (colors.TryGetValue("text-selected", out c)) _textHover = c;
        BackColor = _bgPrimary;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var hPad = this.ScaleForDpi(16);
        var radius = this.ScaleForDpi(8);
        var gap = this.ScaleForDpi(1);
        var topMargin = this.ScaleForDpi(16);

        // 测量文字
        var textSize = TextRenderer.MeasureText(e.Graphics, ButtonText, _textFont,
            Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        var iconSize = TextRenderer.MeasureText(e.Graphics, FolderIcon, _iconFont,
            Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var totalW = iconSize.Width + gap + textSize.Width + hPad * 2;
        var totalH = Math.Max(iconSize.Height, textSize.Height) + this.ScaleForDpi(8);

        var bgBounds = new Rectangle(
            (ClientSize.Width - totalW) / 2,
            topMargin,
            totalW,
            totalH);
        _buttonBounds = bgBounds;

        // 背景
        if (_hovered)
        {
            using var brush = new SolidBrush(_bgHover);
            SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
        }
        else
        {
            using var brush = new SolidBrush(_accent);
            SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
        }

        var fgColor = _textHover;

        // 图标
        var iconRect = new Rectangle(
            bgBounds.Left + hPad,
            bgBounds.Top + (bgBounds.Height - iconSize.Height) / 2,
            iconSize.Width,
            iconSize.Height);
        TextRenderer.DrawText(e.Graphics, FolderIcon, _iconFont, iconRect, fgColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // 文字
        var textRect = new Rectangle(
            iconRect.Right + gap,
            bgBounds.Top + (bgBounds.Height - textSize.Height) / 2,
            textSize.Width,
            textSize.Height);
        TextRenderer.DrawText(e.Graphics, ButtonText, _textFont, textRect, fgColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var inBounds = _buttonBounds.Contains(e.X, e.Y);
        if (inBounds != _hovered)
        {
            _hovered = inBounds;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered)
        {
            _hovered = false;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _buttonBounds.Contains(e.X, e.Y))
        {
            Focus();
            FolderOpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
