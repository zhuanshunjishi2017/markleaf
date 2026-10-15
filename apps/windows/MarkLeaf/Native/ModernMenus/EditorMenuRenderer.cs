using System.Drawing.Drawing2D;

namespace MarkLeaf.Native.ModernMenus;

/// <summary>Shared surface and columns for root menus, submenus and quick actions.</summary>
internal sealed class EditorMenuRenderer(int dpi, bool dark) : ToolStripRenderer
{
    internal ModernMenuTheme Theme { get; } = SystemInformation.HighContrast
        ? new(SystemColors.Menu, SystemColors.Highlight, SystemColors.MenuText, SystemColors.MenuText,
            SystemColors.MenuText, SystemColors.GrayText, SystemColors.Highlight)
        : ModernMenuTheme.Create(dark);

    private int Scale(int value) => (int)Math.Round(value * dpi / 96.0);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Background);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown) return;
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = Rounded(bounds, Scale(7));
        using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.MenuText
            : dark ? Color.FromArgb(75, 75, 75) : Color.FromArgb(215, 215, 215));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) => DrawHighlight(e, false);
    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) =>
        DrawHighlight(e, e.Item is ToolStripButton { Checked: true });

    private void DrawHighlight(ToolStripItemRenderEventArgs e, bool check)
    {
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed || check)) return;
        var bounds = new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1);
        if (e.Item is ToolStripMenuItem) bounds.Inflate(-Scale(2), -Scale(1));
        using var path = Rounded(bounds, Scale(4));
        using var brush = new SolidBrush(e.Item.Selected || e.Item.Pressed ? Theme.Hover
            : SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(dark ? 64 : 32, Theme.Accent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    private Color TextColor(ToolStripItem item) => TextColor(item, Theme, SystemInformation.HighContrast);

    internal static Color TextColor(ToolStripItem item, ModernMenuTheme theme, bool highContrast) =>
        !item.Enabled ? theme.DisabledText
        : highContrast && (item.Selected || item.Pressed || item is ToolStripButton { Checked: true })
            ? SystemColors.HighlightText
        : item is ToolStripButton { Checked: true } ? theme.Accent : theme.Text;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        if (e.Item is ToolStripButton)
        {
            TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Item.Font,
                new Rectangle(Point.Empty, e.Item.Size), TextColor(e.Item), flags | TextFormatFlags.HorizontalCenter);
            return;
        }
        if (e.Item.Tag is not EditorMenuEntry entry)
        {
            base.OnRenderItemText(e);
            return;
        }
        // WinForms requests text twice when a shortcut exists. Paint both columns
        // only for the label pass so neither text is overdrawn.
        if (e.Text != e.Item.Text) return;
        var color = TextColor(e.Item);
        var left = Scale(28);
        var right = e.Item.Width - Scale(20);
        var shortcut = e.Item is ToolStripMenuItem { ShowShortcutKeys: true } menu ? menu.ShortcutKeyDisplayString : "";
        var shortcutWidth = string.IsNullOrEmpty(shortcut) ? 0
            : TextRenderer.MeasureText(shortcut, e.Item.Font, Size.Empty, flags).Width;
        var labelWidth = right - left - (shortcutWidth > 0 ? shortcutWidth + Scale(18) : 0);
        TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Item.Font,
            new Rectangle(left, 0, Math.Max(1, labelWidth), e.Item.Height), color, flags | TextFormatFlags.EndEllipsis);
        if (shortcutWidth > 0)
            TextRenderer.DrawText(e.Graphics, shortcut, e.Item.Font,
                new Rectangle(right - shortcutWidth, 0, shortcutWidth, e.Item.Height),
                e.Item.Enabled ? Theme.SecondaryText : Theme.DisabledText, flags | TextFormatFlags.Right);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is null) return;
        var x = e.Item.Width - Scale(11);
        var y = e.Item.Height / 2;
        using var pen = new Pen(TextColor(e.Item), Math.Max(1, Scale(1)))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var direction = e.Direction == ArrowDirection.Left ? -1 : 1;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [new Point(x - direction * Scale(2), y - Scale(3)),
            new Point(x + direction * Scale(1), y), new Point(x - direction * Scale(2), y + Scale(3))]);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var x = Scale(9);
        var y = e.Item.Height / 2;
        using var pen = new Pen(TextColor(e.Item), Math.Max(1.5f, Scale(1)))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [new Point(x - Scale(3), y), new Point(x - Scale(1), y + Scale(2)),
            new Point(x + Scale(4), y - Scale(3))]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Separator);
        if (e.Vertical)
        {
            var x = e.Item.Width / 2;
            e.Graphics.DrawLine(pen, x, Scale(6), x, e.Item.Height - Scale(6));
        }
        else
        {
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, Scale(8), y, e.Item.Width - Scale(8), y);
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
