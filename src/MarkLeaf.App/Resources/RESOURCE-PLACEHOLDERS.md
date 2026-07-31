# MarkLeaf 正式应用资源占位

阶段 1 不引入正式图片、应用图标或工具栏图标，避免使用临时资源污染后续设计。

后续资源位置：

- `Resources/App/App.ico`：正式应用图标；阶段 11 完成。
- `Resources/Toolbar/Light/`：浅色工具栏 SVG 与 16/20/24/32 px PNG；工具栏命令阶段加入。
- `Resources/Toolbar/Dark/`：深色资源，推迟到完整深色主题阶段。
- `Resources/Bitmaps/`：空状态和帮助插图；当前加载占位使用原生控件，不需要图片。
- `Resources/ResourceManifest.json`：资源尺寸、主题和用途清单。
- `Resources/Licenses/Icons.txt`：图标来源、版本和许可证。

工作区文件与文件夹图标后续通过 Windows Shell API 获取，不在仓库内放置假图标。

