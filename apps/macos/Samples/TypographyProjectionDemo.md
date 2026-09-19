# 排版投影验收样张

这一段用于检查中文与英文混排时的全局断行效果。MarkLeaf 正在尝试用自研排版引擎处理整段最优断行，而不是让浏览器逐行贪心换行。观察这里的中英边界、标点、数字与英文单词是否能够共同参与两端对齐，同时注意行末是否出现突兀的空白或过度拉伸。我们希望排版结果更接近出版级文本，而不是只把字符填满一行。The quick brown fox jumps over the lazy dog while 1234567890 keeps a long Latin sequence moving across the measure.

中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文English中文  
English中文English中文English中文English中文English中文English中文English。

标点检查：这是一个测试，包含逗号、句号、顿号、冒号、分号、问号、感叹号。它还有（全角括号）、「引号」、《书名号》以及连续标点：），；？！——……。你应当观察这些符号在行首、行末和相邻位置的表现，特别是它们能否保持自然的视觉节奏。

Typography projection is designed to redistribute space across an entire paragraph. A single long paragraph may contain ordinary spaces, multiple consecutive words, punctuation, quoted phrases such as "global optimal line breaking", and numeric values like 3.141592653589793. The goal is to avoid a lonely word at the end of a line while also preventing an overly tight measure.

这一段重复使用相近长度的短语，帮助你在不同窗口宽度下观察行尾稳定性。全局最优断行会同时考虑整段的松紧程度，而不是只看当前行还能容纳多少字符。窗口变窄时，断点会重新分布；窗口变宽时，原本拥挤的行应当获得更自然的呼吸空间。请反复拖动窗口边缘，检查重排是否有明显跳动。

排版引擎的第一阶段只处理普通段落。标题、代码块、公式、图片和表格会保持原有行为，因此这段之后的结构主要用于确认它们没有被误处理。

## 中英混排样例

OpenType MATH 与 CoreText 是两个不同层级的能力。MarkLeaf 当前先解决正文段落的 Knuth–Plass 断行，再逐步处理 CJK 标点压缩、避头尾和悬挂标点。Typography is only one layer of the document experience; editing, search, export, and workspace navigation must remain stable.

## 极端宽度

这一行很长，而且包含很多不同的内容：中文、English、数字 2026、全角标点、半角标点、quoted text、连续词汇、以及 intentionally-supercalifragilisticexpialidocious-like compound terminology。它可以帮助检查窄窗口下的 fallback 与宽窗口下的自然断行。

## 阅读节奏

好的两端对齐不只是让左右边缘齐平。它还需要考虑字间距、词间距、行尾标点、标点压缩和 CJK/Latin 边界。第一阶段的重点是把正文段落的换行决策与导出保持一致；后续阶段会继续细化标点策略和复杂 inline 内容。