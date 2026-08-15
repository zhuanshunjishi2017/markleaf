# MarkLeaf 原型验证

这是一个用于验证 **Markdown 可视化编辑**、*中文斜体示例*、中文输入和安全导出的阶段 0 文档。

> 磁盘上的 Markdown 是最终持久化格式。

## 检查清单

- [x] 加载 Markdown
- [ ] 使用中文输入法继续编辑
- [ ] 验证撤销与重做

## 表格

| 能力 | 预期 |
| :--- | :--- |
| 标题与段落 | 保留语义 |
| 任务列表 | 可交互 |
| 中文标点 | 正常输入 |

## 代码

```csharp
var editor = "MarkLeaf";
Console.WriteLine(editor);
```

图片资源占位：`prototype.assets/image-placeholder.png`（阶段 0 不提供实际图片）。
