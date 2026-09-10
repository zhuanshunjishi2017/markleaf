# MarkLeaf for VS Code

打开 Markdown 文件即可阅读和编辑；如果已有其他默认编辑器关联，使用 **Reopen Editor With… → MarkLeaf → Configure Default Editor**。

- **Ctrl/Cmd+Shift+V**：渲染与源码切换。保存、撤销和重做由 VS Code 文档管理。
- **格式**：标题、文字格式、列表、提示框、表格、公式、Mermaid、脚注和 Front Matter。
- **格式刷**：选择带格式的文字，点击格式刷，再拖选目标；Escape 取消。
- **图片**：从文件选择，或直接粘贴/拖放图片；默认保存到文档旁的 assets 目录。选中图片后使用“编辑 → 当前图片操作”。
- **编辑**：复制为 Markdown / 纯文本 / HTML，粘贴纯文本，查找和替换。常规 Ctrl/Cmd+C 保留选区的文本和 HTML。
- **视图**：大纲、专注段落、打字机滚动、缩放、默认极简排版，也可选择九种排版和原有配色主题。
- **快捷键**：“视图 → 快捷键…”可搜索、录入、清除或恢复格式操作键位，保存后在格式菜单同步显示。公式默认未绑定；绑定后可在渲染编辑区直接打开公式辅助。
- **设置**：在 VS Code 设置中搜索 `@ext:markleaf.markleaf`，调整字体、行高、间距、图片处理、Markdown 标记及 `markleaf.shortcuts`。源码切换、查找等宿主操作通过 VS Code Keyboard Shortcuts 自定义。

升级后若快捷键面板提示配置尚未注册，请先保存文档，再运行 **Developer: Reload Window（开发人员: 重新加载窗口）**，然后重新录入并保存键位。

阅读模式保留复制、查找、链接和大纲导航。修改选区格式、插入块和替换内容需要编辑模式。公式及 Mermaid 双击后使用共享源码编辑控件。

文件/文件夹、最近文件、标签页、自动保存、恢复、编码、换行、窗口布局、主题安装和扩展更新使用 VS Code 自身功能。MarkLeaf 只维护渲染视图状态，不另建文件数据库。

**导出…**：PDF、完整 HTML、PNG/JPG 长图、预览、打印与按上次设置导出。默认极简排版；PDF/图片/预览/打印使用已安装 Chrome/Edge，HTML 文件可直接生成。浏览器缺失时会提示设置 `markleaf.exportBrowserPath`。导出不修改 Markdown；取消后已写出的分片会列在结果中。
