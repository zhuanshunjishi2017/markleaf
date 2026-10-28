// 四语言 L10n 测试:对齐 macOS L10n 语义——以简体中文原文为键,
// zh-Hans 直返原文,其余语言查表,缺键回退原文;未知语言返回 None。
use markleaf::l10n::{tr, SUPPORTED_LANGUAGES};

const MENU_KEYS: &[&str] = &[
    "文件", "编辑", "视图", "格式", "插入", "导出", "帮助", "设置",
    "打开", "保存", "另存为", "新建文档", "打印", "工作区", "大纲",
    "关闭标签", "查找", "替换", "加粗", "斜体", "源码模式",
    "导出 HTML", "导出 PDF", "导出长图", "深色模式", "浅色模式", "跟随系统",
    "搜索文档", "全部保存", "新建窗口", "上一次编辑位置", "行数", "字符数",
];

#[test]
fn zh_hans_returns_original_text() {
    for key in MENU_KEYS {
        assert_eq!(tr("zh-Hans", key).as_deref(), Some(*key), "zh-Hans 直返原文:{key}");
    }
}

#[test]
fn all_languages_translate_every_shell_key() {
    for lang in SUPPORTED_LANGUAGES {
        if *lang == "zh-Hans" {
            continue;
        }
        for key in MENU_KEYS {
            let value = tr(lang, key);
            assert!(value.is_some(), "{lang} 缺键 {key}");
            assert_ne!(value.unwrap(), key, "{lang} 键 {key} 未翻译(回退原文)");
        }
    }
}

#[test]
fn unknown_key_falls_back_to_original_for_known_language() {
    // macOS 语义:tables[language]?[text] ?? text——缺键不返回 None。
    assert_eq!(tr("en-US", "尚未收录的词条").as_deref(), Some("尚未收录的词条"));
}

#[test]
fn unknown_language_returns_none() {
    assert_eq!(tr("xx-XX", "打开"), None);
}

#[test]
fn zh_hant_differs_from_zh_hans_on_divergent_words() {
    assert_ne!(tr("zh-Hans", "文件").as_deref(), tr("zh-Hant", "文件").as_deref());
    assert_eq!(tr("zh-Hant", "设置").as_deref(), Some("設定"));
}
