// 四语言本地化(zh-Hans/zh-Hant/en-US/ja-JP),语义对齐 macOS L10n:
// 以简体中文原文为键,zh-Hans 直返原文,其余查表,缺键回退原文。
// v1 覆盖壳层 UI 字符串;内核/前端文案由内核自身 L10n 承担(localizeFindBar)。
pub const SUPPORTED_LANGUAGES: &[&str] = &["zh-Hans", "zh-Hant", "en-US", "ja-JP"];

/// 解析界面语言:设置显式值优先,否则按 glib locale 名映射;兜底 zh-Hans。
pub fn resolve_language(display_language: &str) -> String {
    if SUPPORTED_LANGUAGES.contains(&display_language) {
        return display_language.to_string();
    }
    for name in gtk::glib::language_names() {
        let lowered = name.to_string().to_ascii_lowercase();
        let mapped = if lowered.starts_with("zh_tw") || lowered.starts_with("zh-hant") {
            Some("zh-Hant")
        } else if lowered.starts_with("zh") {
            Some("zh-Hans")
        } else if lowered.starts_with("ja") {
            Some("ja-JP")
        } else if lowered.starts_with("en") {
            Some("en-US")
        } else {
            None
        };
        if let Some(lang) = mapped {
            return lang.to_string();
        }
    }
    "zh-Hans".to_string()
}

/// 翻译;未知语言返回 None,已知语言缺键回退原文(macOS tables[lang]?[text] ?? text)。
pub fn tr(language: &str, key: &str) -> Option<String> {
    match language {
        "zh-Hans" => Some(key.to_string()),
        // 已知语言查表,缺键回退原文(macOS tables[lang]?[text] ?? text);
        // 未知语言返回 None。
        lang => {
            let (_, table) = TABLES.iter().find(|(name, _)| *name == lang)?;
            Some(
                table
                    .iter()
                    .find(|(k, _)| *k == key)
                    .map(|(_, value)| (*value).to_string())
                    .unwrap_or_else(|| key.to_string()),
            )
        }
    }
}

type Table = &'static [(&'static str, &'static str)];

const ZH_HANT: Table = &[
    ("文件", "檔案"),
    ("编辑", "編輯"),
    ("视图", "檢視"),
    ("格式", "格式"),
    ("插入", "插入"),
    ("导出", "匯出"),
    ("帮助", "輔助說明"),
    ("设置", "設定"),
    ("打开", "打開"),
    ("保存", "儲存"),
    ("另存为", "另儲為"),
    ("新建文档", "新增文件"),
    ("打印", "列印"),
    ("工作区", "工作區"),
    ("大纲", "大綱"),
    ("关闭标签", "關閉標籤頁"),
    ("查找", "尋找"),
    ("替换", "取代"),
    ("加粗", "粗體"),
    ("斜体", "斜體"),
    ("源码模式", "原始碼模式"),
    ("导出 HTML", "匯出 HTML"),
    ("导出 PDF", "匯出 PDF"),
    ("导出长图", "匯出長圖"),
    ("深色模式", "深色模式"),
    ("浅色模式", "淺色模式"),
    ("跟随系统", "跟隨系統"),
    ("搜索文档", "搜尋文件"),
    ("全部保存", "全部儲存"),
    ("新建窗口", "新增視窗"),
    ("行数", "行數"),
    ("字符数", "字元數"),
];

const EN_US: Table = &[
    ("文件", "File"),
    ("编辑", "Edit"),
    ("视图", "View"),
    ("格式", "Format"),
    ("插入", "Insert"),
    ("导出", "Export"),
    ("帮助", "Help"),
    ("设置", "Settings"),
    ("打开", "Open"),
    ("保存", "Save"),
    ("另存为", "Save As"),
    ("新建文档", "New Document"),
    ("打印", "Print"),
    ("工作区", "Workspace"),
    ("大纲", "Outline"),
    ("关闭标签", "Close Tab"),
    ("查找", "Find"),
    ("替换", "Replace"),
    ("加粗", "Bold"),
    ("斜体", "Italic"),
    ("源码模式", "Source Mode"),
    ("导出 HTML", "Export HTML"),
    ("导出 PDF", "Export PDF"),
    ("导出长图", "Export Long Image"),
    ("深色模式", "Dark Mode"),
    ("浅色模式", "Light Mode"),
    ("跟随系统", "Follow System"),
    ("搜索文档", "Search Documents"),
    ("全部保存", "Save All"),
    ("新建窗口", "New Window"),
    ("行数", "Lines"),
    ("字符数", "Characters"),
];

const JA_JP: Table = &[
    ("文件", "ファイル"),
    ("编辑", "編集"),
    ("视图", "表示"),
    ("格式", "フォーマット"),
    ("插入", "挿入"),
    ("导出", "書き出し"),
    ("帮助", "ヘルプ"),
    ("设置", "設定"),
    ("打开", "開く"),
    ("保存", "保存"),
    ("另存为", "別名で保存"),
    ("新建文档", "新規書類"),
    ("打印", "プリント"),
    ("工作区", "ワークスペース"),
    ("大纲", "アウトライン"),
    ("关闭标签", "タブを閉じる"),
    ("查找", "検索"),
    ("替换", "置換"),
    ("加粗", "太字"),
    ("斜体", "斜体"),
    ("源码模式", "ソースモード"),
    ("导出 HTML", "HTML を書き出す"),
    ("导出 PDF", "PDF に書き出す"),
    ("导出长图", "長画像を書き出す"),
    ("深色模式", "ダークモード"),
    ("浅色模式", "ライトモード"),
    ("跟随系统", "システムに従う"),
    ("搜索文档", "書類を検索"),
    ("全部保存", "すべて保存"),
    ("新建窗口", "新規ウインドウ"),
    ("行数", "行数"),
    ("字符数", "文字数"),
    ("备注", "ノート"),
    ("提示", "ヒント"),
    ("重要", "重要"),
    ("警告", "警告"),
    ("注意", "注意"),
    ("升为标题", "見出しへ昇格"),
    ("降为标题", "見出しへ降格"),
    ("区分大小写", "大文字小文字を区別"),
    ("全部替换", "すべて置換"),
    ("关闭", "閉じる"),
    ("下一个", "次へ"),
    ("上一个", "前へ"),
];

const TABLES: &[(&str, Table)] = &[
    ("zh-Hant", ZH_HANT),
    ("en-US", EN_US),
    ("ja-JP", JA_JP),
];
