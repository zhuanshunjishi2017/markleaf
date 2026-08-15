using System.Diagnostics;
using System.Reflection;
using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class AboutDialog : Form
{
    private const string RepoOwner = "zhuanshunjishi2017";
    private const string RepoName = "markleaf";
    private const string AuthorName = "zhuanshunjishi2017";

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
        Size = new Size(this.ScaleForDpi(469), this.ScaleForDpi(286));
        MinimumSize = new Size(this.ScaleForDpi(326), this.ScaleForDpi(274));
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(this.ScaleForDpi(14), this.ScaleForDpi(14), this.ScaleForDpi(14), this.ScaleForDpi(9));

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(126)));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var iconPicture = new PictureBox
        {
            Size = new Size(this.ScaleForDpi(110), this.ScaleForDpi(110)),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, this.ScaleForDpi(5), this.ScaleForDpi(11), 0),
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

        infoPanel.Controls.Add(NewLabel("MarkLeaf", new Font("Times New Roman", 18F, FontStyle.Bold)), 0, 0);
        infoPanel.Controls.Add(NewLabel(Loc.Get("dialog.aboutDescription"), new Font("Times New Roman", 9F)), 0, 1);
        infoPanel.Controls.Add(NewSeparator(), 0, 2);
        infoPanel.Controls.Add(NewLabel(Loc.Format("dialog.aboutVersion", AppVersion)), 0, 3);
        infoPanel.Controls.Add(NewLabel(Loc.Format("dialog.aboutDate", BuildDate)), 0, 4);

        infoPanel.Controls.Add(NewSeparator(), 0, 5);
        infoPanel.Controls.Add(NewLabel(Loc.Format("dialog.aboutAuthor", AuthorName)), 0, 6);

        var repoLink = new LinkLabel
        {
            Text = $"https://github.com/{RepoOwner}/{RepoName}",
            AutoSize = true,
            Margin = new Padding(0, this.ScaleForDpi(2), 0, 0),
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
            Margin = new Padding(0, this.ScaleForDpi(7), 0, 0),
        };

        var okButton = new Button
        {
            Text = Loc.Get("common.ok"),
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(86), this.ScaleForDpi(26)),
            Padding = new Padding(this.ScaleForDpi(7), this.ScaleForDpi(2), this.ScaleForDpi(7), this.ScaleForDpi(2)),
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

    private Label NewLabel(string text, Font? font = null, float fontSize = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, this.ScaleForDpi(2), 0, this.ScaleForDpi(2)),
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

    private Label NewSeparator()
    {
        return new Label
        {
            AutoSize = false,
            Height = 1,
            Margin = new Padding(0, this.ScaleForDpi(6), 0, this.ScaleForDpi(6)),
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
