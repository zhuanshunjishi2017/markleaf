using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class OpenFolderPrompt : Control
{
    private Color _bgSelected = SystemColors.Highlight;
    private Color _bgSelectedHover = SystemColors.HotTrack;
    private Color _bgPrimary = Color.White;
    private Color _textSecondary = Color.FromArgb(0x55, 0x55, 0x55);

    private bool _hovered;
    private Font _textFont = new("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _labelFont = new("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

    private static string LabelText => Loc.Get("sidebar.noWorkspace");
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
        if (colors.TryGetValue("bg-selected", out c)) _bgSelected = c;
        if (colors.TryGetValue("bg-selected-hover", out c)) _bgSelectedHover = c;
        if (colors.TryGetValue("text-secondary", out c)) _textSecondary = c;
        BackColor = _bgPrimary;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var hPad = this.ScaleForDpi(12);
        var radius = this.ScaleForDpi(8);
        var labelGap = this.ScaleForDpi(10);

        // 测量文字
        var textSize = TextRenderer.MeasureText(e.Graphics, ButtonText, _textFont,
            Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        var labelSize = TextRenderer.MeasureText(e.Graphics, LabelText, _labelFont,
            Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var buttonW = textSize.Width + hPad * 2;
        var buttonH = textSize.Height + this.ScaleForDpi(8);

        // 标签在上、按钮在下，整体垂直居中（与 macOS 空状态一致）
        var groupH = labelSize.Height + labelGap + buttonH;
        var groupTop = Math.Max(this.ScaleForDpi(16), (ClientSize.Height - groupH) / 2);
        var labelBounds = new Rectangle(0, groupTop, ClientSize.Width, labelSize.Height);
        var bgBounds = new Rectangle(
            (ClientSize.Width - buttonW) / 2,
            labelBounds.Bottom + labelGap,
            buttonW,
            buttonH);
        _buttonBounds = bgBounds;

        TextRenderer.DrawText(e.Graphics, LabelText, _labelFont, labelBounds, _textSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        // 按钮背景
        if (_hovered)
        {
            using var brush = new SolidBrush(_bgSelectedHover);
            SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
        }
        else
        {
            using var brush = new SolidBrush(_bgSelected);
            SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
        }

        var fgColor = _textSecondary;
        var textRect = new Rectangle(
            bgBounds.Left + hPad,
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
            _textFont.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
