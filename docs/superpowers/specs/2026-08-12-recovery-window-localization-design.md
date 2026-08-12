# Recovery Window Localization Design

## Problem

The macOS recovery window localizes its title, table headings, and buttons through `L10n`, but constructs its introductory sentence as a raw Simplified Chinese string. As a result, English, Traditional Chinese, and Japanese interfaces show a Chinese sentence beneath an otherwise translated title.

The Simplified Chinese sentence also contains the parenthetical phrase `（上次异常退出遗留）`, which the product requirement removes.

## Language Scope

Windows currently supports four display languages: Simplified Chinese (`zh-CN`), Traditional Chinese (`zh-TW`), English (`en-US`), and Japanese (`ja-JP`). The multi-language set was introduced before Windows 1.1.4; Windows 1.1.4 added live language refresh rather than a fifth language.

The macOS port already supports the same four language families as `zh-Hans`, `zh-Hant`, `en`, and `ja`. This fix therefore updates all four existing languages and adds no new language option.

## Chosen Design

Replace the hard-coded recovery sentence with singular and plural canonical localization keys, following the current Windows recovery-dialog pattern:

- Singular: `检测到 1 个未保存的文档。请选择要恢复的快照：`
- Plural: `检测到 %d 个未保存的文档。请选择要恢复的快照：`

Translations:

- Simplified Chinese: `检测到 1 个未保存的文档。请选择要恢复的快照：` / `检测到 %d 个未保存的文档。请选择要恢复的快照：`
- Traditional Chinese: `偵測到 1 個未儲存的文件。請選擇要復原的快照：` / `偵測到 %d 個未儲存的文件。請選擇要復原的快照：`
- English: `Found 1 unsaved document. Choose a snapshot to recover:` / `Found %d unsaved documents. Choose a snapshot to recover:`
- Japanese: `1 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください：` / `%d 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください：`

The recovery controller will select the singular key when the snapshot count is one, otherwise format the plural key with `L10n.f(...)`. The obsolete key containing the abnormal-exit parenthetical will be removed from every non-Simplified-Chinese translation table. This yields natural English without introducing a general pluralization framework.

## Component Changes

### `RecoveryWindowController`

- Replace the raw interpolated Chinese sentence with singular/plural localized copy.
- Keep the existing localized title, column headings, and buttons unchanged.

### `L10n`

- Add both new canonical sentences to Japanese, Traditional Chinese, and English tables.
- Remove the obsolete parenthetical sentence key from those tables.
- Simplified Chinese continues to use the canonical key itself as its displayed value.

## Testing

Automated tests will verify:

- Singular and plural recovery messages contain no abnormal-exit parenthetical in Simplified Chinese.
- English, Traditional Chinese, and Japanese return the expected singular and plural sentences.
- Every non-Simplified-Chinese translation table contains both new canonical keys and no longer contains the obsolete key.
- A constructed recovery window uses localized introductory copy rather than hard-coded Chinese.
- Existing localization completeness and full macOS test suites remain green.

Manual verification will launch the installed app with a pending recovery snapshot and inspect the recovery window in Simplified Chinese and English. In Chinese, the parenthetical must be absent; in English, both the title and introductory sentence must be English.

## Non-Goals

- Adding a fifth display language.
- Introducing a general pluralization framework.
- Changing snapshot discovery, recovery, discard, or save behavior.
- Redesigning the recovery window layout.

## Acceptance Criteria

- Simplified Chinese shows `检测到 N 个未保存的文档。请选择要恢复的快照：` with no parenthetical phrase.
- English, Traditional Chinese, and Japanese show no Simplified Chinese introductory copy.
- Existing recovery actions and table content behave unchanged.
- The installed MarkLeaf application passes automated tests and real recovery-window verification.
