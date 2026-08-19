using System.Diagnostics;
using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class AboutDialog : Form
{
    private const string RepoOwner = "zhuanshunjishi2017";
    private const string RepoName = "markleaf";
    private const string AuthorName = "fcz";

    private static string AppVersion =>
        typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";

    private static string BuildDate
    {
        get
        {
            try
            {
                var assemblyPath = typeof(AboutDialog).Assembly.Location;
                return File.GetLastWriteTime(assemblyPath).ToString("yyyy-MM-dd");
            }
            catch
            {
                return "----";
            }
        }
    }

    public AboutDialog()
    {
        Text = Loc.Get("dialog.aboutTitle");
        BackColor = SystemColors.Window;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(this.ScaleForDpi(300), this.ScaleForDpi(336));
        MinimumSize = new Size(this.ScaleForDpi(280), this.ScaleForDpi(336));
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(this.ScaleForDpi(16), this.ScaleForDpi(18), this.ScaleForDpi(16), this.ScaleForDpi(14));

        var iconPicture = new PictureBox
        {
            Size = new Size(this.ScaleForDpi(92), this.ScaleForDpi(92)),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 0, this.ScaleForDpi(8)),
            Anchor = AnchorStyles.None,
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "App.png");
        if (File.Exists(iconPath))
        {
            iconPicture.Image = Image.FromFile(iconPath);
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(104)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(34)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(14)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(28)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(iconPicture, 0, 0);
        layout.Controls.Add(NewLabel("MarkLeaf", new Font("Times New Roman", 16F, FontStyle.Bold)), 0, 1);
        layout.Controls.Add(NewLabel(Loc.Get("dialog.aboutDescription"), new Font("Times New Roman", 11F, FontStyle.Regular)), 0, 2);
        layout.Controls.Add(NewSeparator(), 0, 3);
        layout.Controls.Add(NewLabel($"{Loc.Format("dialog.aboutVersion", AppVersion)}    {Loc.Format("dialog.aboutDate", BuildDate)}"), 0, 4);
        layout.Controls.Add(NewLabel(Loc.Format("dialog.aboutAuthor", AuthorName)), 0, 5);

        var repoLink = new LinkLabel
        {
            Text = "GitHub 仓库",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = Color.FromArgb(9, 105, 218),
            ActiveLinkColor = Color.FromArgb(9, 105, 218),
            VisitedLinkColor = Color.FromArgb(9, 105, 218),
        };
        repoLink.LinkClicked += (_, _) =>
        {
            OpenUrl($"https://github.com/{RepoOwner}/{RepoName}");
        };
        layout.Controls.Add(repoLink, 0, 6);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);

        Controls.Add(layout);
    }

    private Label NewLabel(string text, Font? font = null, float fontSize = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleCenter,
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

    private Control NewSeparator()
    {
        return new Panel
        {
            Anchor = AnchorStyles.None,
            BackColor = SystemColors.ControlDark,
            Size = new Size(this.ScaleForDpi(180), 1),
            Margin = Padding.Empty,
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
