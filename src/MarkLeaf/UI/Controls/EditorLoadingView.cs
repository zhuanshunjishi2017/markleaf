using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class EditorLoadingView : TableLayoutPanel
{
    private readonly Label _title;
    private readonly ProgressBar _progress;
    private readonly Label _detail;
    private readonly Button _retryButton;

    public event EventHandler? RetryRequested;

    public EditorLoadingView()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ColumnCount = 1;
        RowCount = 6;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _title = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 16F, FontStyle.Regular),
            ForeColor = SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 10),
            Visible = false,
        };
        _progress = new ProgressBar
        {
            Anchor = AnchorStyles.None,
            Width = 260,
            Height = 5,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 32,
            Margin = new Padding(0, 0, 0, 12),
            Visible = false,
        };
        _detail = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = SystemColors.GrayText,
            Visible = false,
        };
        _retryButton = new Button
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = Loc.Get("common.retry"),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 14, 0, 0),
            Visible = false,
        };
        _retryButton.Click += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(new Panel(), 0, 0);
        Controls.Add(_title, 0, 1);
        Controls.Add(_progress, 0, 2);
        Controls.Add(_detail, 0, 3);
        Controls.Add(_retryButton, 0, 4);
        Controls.Add(new Panel(), 0, 5);
    }

    public void ShowLoading(string title, string detail)
    {
        _title.Visible = false;
        _detail.Visible = false;
        _progress.Visible = false;
        _retryButton.Visible = false;
    }

    public void ShowFailure(string detail)
    {
        _title.Text = Loc.Get("editor.failed");
        _title.Visible = true;
        _detail.Text = detail;
        _detail.Visible = true;
        _progress.Visible = false;
        _retryButton.Visible = true;
    }
}
