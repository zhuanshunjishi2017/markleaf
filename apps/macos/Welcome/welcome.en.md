# Welcome to MarkLeaf

MarkLeaf is a Markdown editor focused on writing and typography. This is an editable introduction: change anything here and watch the result as you explore the app.

## Start writing

1. Choose File > New Document, or press `Command+N`.
2. Use the `</>` button at the bottom of the window to switch between Visual and Source modes.
3. Press `Command+S` to save. The first save asks for a file name and location.
4. Open a folder as a workspace to browse, search, and manage documents from the sidebar.

| Action | Shortcut |
| --- | --- |
| New document | `Command+N` |
| Open document | `Command+O` |
| Save document | `Command+S` |
| Find and replace | `Command+F` |
| Undo | `Command+Z` |
| Redo | `Shift+Command+Z` |

## Markdown basics

### Headings and the outline

In Source mode, use `#` characters to create headings:

```markdown
# Heading 1
## Heading 2
### Heading 3
```

Headings appear automatically in the outline. Select an outline item to jump to that section.

### Inline formatting

- `**bold**` becomes **bold**
- `*italic*` becomes *italic*
- `~~strikethrough~~` becomes ~~strikethrough~~
- `` `inline code` `` becomes `inline code`
- Underline, links, and the Format Painter are also available

### Lists

1. Write the content
2. Review the formatting
3. Save and export

- [x] Open MarkLeaf
- [ ] Create your first document
- [ ] Try exporting to HTML or PDF

When the caret is in a list item, press `Tab` to indent or `Shift+Tab` to outdent.

### Quotes and code blocks

> A good tool lets you see your content rather than the tool itself.

```swift
let message = "Hello, MarkLeaf!"
print(message)
```

## Rich content

### Math

Write inline math as `$E=mc^2$` to render $E=mc^2$.

Longer equations can stand on their own:

$$\int_{-\infty}^{+\infty} e^{-x^2}\,dx=\sqrt{\pi}$$

### Footnotes

A closed reference in body text is rendered as a superscript[^1]. Hold `Command` while clicking to move between a reference and its definition.

[^1]: Footnotes support **bold**, *italic*, ~~strikethrough~~, `inline code`, [links](https://github.com/zhuanshunjishi2017/markleaf), and inline math such as $x^2+y^2$.

### Mermaid diagrams

```mermaid
flowchart LR
    A[Start writing] --> B[Organize]
    B --> C[Review]
    C --> D[Save or export]
```

### Tables

| Feature | Purpose |
| --- | --- |
| Visual editing | Focus on content without handling every marker |
| Source editing | Control the Markdown text precisely |
| Workspace | Manage documents in the same folder |
| Outline | Browse the structure and jump quickly |

## Make it yours

Use the View menu to change typography styles, color themes, zoom, the sidebar, and Focus Mode. Switching between Workspace and Outline uses the current theme and can reverse smoothly when you change direction quickly.

The status bar shows the file encoding, line endings, editing mode, and zoom. Save important work before changing encoding: Reload reads the file again with the selected encoding, while Save Encoding preserves the current text and saves it using that encoding.

You can close this document and start writing, or keep editing it to try the features above.
