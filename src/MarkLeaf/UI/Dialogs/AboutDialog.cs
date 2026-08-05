using System.Diagnostics;
using System.Reflection;

namespace MarkLeaf.UI.Dialogs;

internal sealed class AboutDialog : Form
{
    private const string RepoOwner = "zhuanshunjishi2017";
    private const string RepoName = "markleaf";
    private const string AuthorName = "zhuanshunjishi2017";
    private static string AppVersion =>
        typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    private const string UpdateDate = "2026-08-04";

    public AboutDialog()
    {
        Text = "关于 MarkLeaf";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(750, 480);
        MinimumSize = new Size(520, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(24, 24, 24, 16);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconPicture = new PictureBox
        {
            Size = new Size(128, 128),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 8, 24, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "App.png");
        if (File.Exists(iconPath))
        {
            iconPicture.Image = Image.FromFile(iconPath);
        }

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(0),
        };

        infoPanel.Controls.Add(NewLabel("MarkLeaf", new Font(Font, FontStyle.Bold), 14.25F), 0, 0);
        infoPanel.Controls.Add(NewLabel("Windows 原生轻量化 Markdown 编辑器"), 0, 1);
        infoPanel.Controls.Add(NewSeparator(), 0, 2);
        infoPanel.Controls.Add(NewLabel($"版本： {AppVersion}"), 0, 3);
        infoPanel.Controls.Add(NewLabel($"日期： {UpdateDate}"), 0, 4);

        infoPanel.Controls.Add(NewSeparator(), 0, 5);
        infoPanel.Controls.Add(NewLabel($"作者： {AuthorName}"), 0, 6);

        var repoLink = new LinkLabel
        {
            Text = $"https://github.com/{RepoOwner}/{RepoName}",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
            LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = Color.FromArgb(9, 105, 218),
            ActiveLinkColor = Color.FromArgb(9, 105, 218),
            VisitedLinkColor = Color.FromArgb(9, 105, 218),
        };
        repoLink.LinkClicked += (_, _) =>
        {
            OpenUrl($"https://github.com/{RepoOwner}/{RepoName}");
        };
        infoPanel.Controls.Add(repoLink, 0, 7);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(0, 12, 0, 0),
        };

        var okButton = new Button
        {
            Text = "确定",
            AutoSize = true,
            MinimumSize = new Size(150, 45),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
        };
        okButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(okButton);

        var outerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        outerLayout.Controls.Add(mainLayout, 0, 0);
        outerLayout.Controls.Add(buttonPanel, 0, 1);
        mainLayout.Controls.Add(iconPicture, 0, 0);
        mainLayout.Controls.Add(infoPanel, 1, 0);
        Controls.Add(outerLayout);
    }

    private static Label NewLabel(string text, Font? font = null, float fontSize = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
            UseMnemonic = false,
        };
        if (font is not null)
        {
            label.Font = fontSize > 0
                ? new Font(font.FontFamily, fontSize, font.Style)
                : font;
        }

        return label;
    }

    private static Label NewSeparator()
    {
        return new Label
        {
            AutoSize = false,
            Height = 1,
            Margin = new Padding(0, 10, 0, 10),
            BorderStyle = BorderStyle.Fixed3D,
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
