using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Prototype;

internal sealed class MainForm : Form
{
    private readonly PrototypeOptions _options;
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly TreeView _outlineTree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly ToolStripStatusLabel _statusLabel = new("正在初始化编辑器...");
    private readonly ToolStripStatusLabel _encodingLabel = new("UTF-8");
    private readonly ToolStripStatusLabel _newlineLabel = new("CRLF");
    private readonly ToolStripStatusLabel _modeLabel = new("可视化模式");
    private readonly Label _loadingLabel = new()
    {
        Dock = DockStyle.Fill,
        Text = "正在启动 MarkLeaf 编辑内核...",
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
    };

    private long _revision;
    private string _markdown = string.Empty;
    private bool _editorReady;
    private string? _pendingExportPath;

    public MainForm(PrototypeOptions options)
    {
        _options = options;
        Text = "MarkLeaf - 原型验证.md";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 640);
        ClientSize = new Size(1360, 840);
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(CreateMainLayout());
        Controls.Add(CreateToolbar());
        Controls.Add(CreateStatusBar());

        Shown += async (_, _) => await InitializeEditorAsync();
    }

    private Control CreateMainLayout()
    {
        var workspaceTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ShowLines = false,
            HideSelection = false,
        };
        var root = workspaceTree.Nodes.Add("阶段 0 工作区");
        root.Nodes.Add("原型验证.md");
        root.Expand();

        var leftPanel = CreateSidePanel("工作区", workspaceTree);
        var rightPanel = CreateSidePanel("大纲", _outlineTree);

        var editorPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        editorPanel.Controls.Add(_webView);
        editorPanel.Controls.Add(_loadingLabel);

        var editorAndOutline = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1140, 760),
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            Panel2MinSize = 180,
            SplitterDistance = 890,
        };
        editorAndOutline.Panel1.Controls.Add(editorPanel);
        editorAndOutline.Panel2.Controls.Add(rightPanel);

        var layout = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1360, 760),
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 1,
            Panel1MinSize = 180,
            SplitterDistance = 220,
        };
        layout.Panel1.Controls.Add(leftPanel);
        layout.Panel2.Controls.Add(editorAndOutline);
        return layout;
    }

    private static Control CreateSidePanel(string title, Control content)
    {
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = title,
            Padding = new Padding(10, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = SystemColors.Control,
        };

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = SystemColors.Window,
        };
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        return panel;
    }

    private Control CreateToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false,
            BackColor = SystemColors.Control,
        };

        AddCommandButton(toolbar, "撤销", "undo");
        AddCommandButton(toolbar, "重做", "redo");
        AddCommandButton(toolbar, "粗体", "toggleBold");
        AddCommandButton(toolbar, "斜体", "toggleItalic");
        AddCommandButton(toolbar, "代码", "toggleCode");
        AddCommandButton(toolbar, "任务", "toggleTaskList");
        AddCommandButton(toolbar, "表格", "insertTable");

        var exportButton = CreateSystemButton("导出 Markdown");
        exportButton.Click += async (_, _) => await ExportWithDialogAsync();
        toolbar.Controls.Add(exportButton);

        return toolbar;
    }

    private void AddCommandButton(Control toolbar, string text, string command)
    {
        var button = CreateSystemButton(text);
        button.Click += (_, _) => Send("command", new { command });
        toolbar.Controls.Add(button);
    }

    private static Button CreateSystemButton(string text)
    {
        return new Button
        {
            AutoSize = true,
            MinimumSize = new Size(62, 28),
            Text = text,
            Padding = new Padding(8, 2, 8, 2),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 0, 6, 0),
        };
    }

    private Control CreateStatusBar()
    {
        var status = new StatusStrip { SizingGrip = false };
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        status.Items.AddRange([_statusLabel, _encodingLabel, _newlineLabel, _modeLabel]);
        return status;
    }

    private async Task InitializeEditorAsync()
    {
        try
        {
            var editorWebPath = Path.Combine(AppContext.BaseDirectory, "EditorWeb");
            var indexPath = Path.Combine(editorWebPath, "index.html");
            var samplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex.md");

            if (!File.Exists(indexPath) || !File.Exists(samplePath))
            {
                throw new FileNotFoundException("Prototype content was not copied to the output directory.");
            }

            _markdown = await File.ReadAllTextAsync(samplePath, Encoding.UTF8);

            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "editor.local",
                editorWebPath,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    Fail($"编辑器页面加载失败：{args.WebErrorStatus}");
                }
            };
            _webView.Source = new Uri("https://editor.local/index.html");
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            var message = EditorProtocol.Deserialize(eventArgs.WebMessageAsJson);
            if (message is null || message.ProtocolVersion != EditorProtocol.Version)
            {
                return;
            }

            if (message.Type != "ready" && !string.Equals(message.DocumentId, _documentId.ToString(), StringComparison.Ordinal))
            {
                return;
            }

            if (message.Type != "ready" && message.Revision < _revision)
            {
                return;
            }

            _revision = Math.Max(_revision, message.Revision);

            switch (message.Type)
            {
                case "ready":
                    _editorReady = true;
                    _loadingLabel.Visible = false;
                    Send("loadDocument", new { markdown = _markdown }, "initial-load");
                    break;
                case "documentLoaded":
                    _statusLabel.Text = "文档已加载";
                    if (!string.IsNullOrWhiteSpace(_options.InitialScrollTarget))
                    {
                        Send("command", new { command = "scrollToHeading", text = _options.InitialScrollTarget });
                    }
                    if (_options.IsSmokeTest)
                    {
                        Send("command", new { command = "appendText", text = " 阶段 0 自动编辑标记。" });
                        Send("command", new { command = "undo" });
                        Send("command", new { command = "redo" });
                        Send("requestSnapshot", requestId: "smoke-export");
                    }
                    break;
                case "dirtyChanged":
                    _statusLabel.Text = $"已修改 · revision {_revision}";
                    break;
                case "outlineChanged":
                    UpdateOutline(message.Payload);
                    break;
                case "snapshot":
                    HandleSnapshot(message);
                    break;
                case "error":
                    Fail(message.Payload.TryGetProperty("message", out var error)
                        ? error.GetString() ?? "编辑器发生未知错误。"
                        : "编辑器发生未知错误。");
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void UpdateOutline(JsonElement payload)
    {
        if (!payload.TryGetProperty("headings", out var headings) || headings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        _outlineTree.BeginUpdate();
        _outlineTree.Nodes.Clear();
        foreach (var heading in headings.EnumerateArray())
        {
            var level = heading.GetProperty("level").GetInt32();
            var text = heading.GetProperty("text").GetString() ?? string.Empty;
            _outlineTree.Nodes.Add($"H{level}  {text}");
        }
        _outlineTree.EndUpdate();
    }

    private void HandleSnapshot(EditorMessage message)
    {
        if (!message.Payload.TryGetProperty("markdown", out var markdownElement))
        {
            return;
        }

        _markdown = markdownElement.GetString() ?? string.Empty;
        _statusLabel.Text = $"快照已确认 · {_markdown.Length} 字符";

        if (message.RequestId == "smoke-export" && _options.SmokeTestOutputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_options.SmokeTestOutputPath)!);
            File.WriteAllText(_options.SmokeTestOutputPath, _markdown, new UTF8Encoding(false));
            BeginInvoke(Close);
            return;
        }

        if (message.RequestId == "manual-export" && _pendingExportPath is not null)
        {
            File.WriteAllText(_pendingExportPath, _markdown, new UTF8Encoding(false));
            _statusLabel.Text = $"已导出：{Path.GetFileName(_pendingExportPath)}";
            _pendingExportPath = null;
        }
    }

    private async Task ExportWithDialogAsync()
    {
        if (!_editorReady)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出 Markdown 快照",
            Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = "MarkLeaf-原型导出.md",
            AddExtension = true,
            DefaultExt = "md",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _pendingExportPath = dialog.FileName;
        Send("requestSnapshot", requestId: "manual-export");

        await Task.CompletedTask;
    }

    private void Send(string type, object? payload = null, string? requestId = null)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsJson(
            EditorProtocol.Serialize(type, _documentId, _revision, payload, requestId));
    }

    private void Fail(string message)
    {
        _loadingLabel.Visible = true;
        _loadingLabel.Text = $"MarkLeaf 编辑器初始化失败\r\n\r\n{message}";
        _statusLabel.Text = "初始化失败";

        if (_options.IsSmokeTest)
        {
            Environment.ExitCode = 1;
            BeginInvoke(Close);
        }
    }
}
