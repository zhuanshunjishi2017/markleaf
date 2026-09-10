import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// 合法内容一律原样保留（nil = 无需改写）：
expect(BoundedTextFieldMonitor.sanitized("1.6", maxFractionDigits: 2) == nil,
       "1.6 must stay untouched")
expect(BoundedTextFieldMonitor.sanitized("1.60", maxFractionDigits: 2) == nil,
       "trailing zero 1.60 must stay untouched (no reformat)")
expect(BoundedTextFieldMonitor.sanitized("1.", maxFractionDigits: 2) == nil,
       "trailing decimal point must stay typeable")
expect(BoundedTextFieldMonitor.sanitized("18.5", maxFractionDigits: 1) == nil,
       "one-decimal margin must stay untouched")
expect(BoundedTextFieldMonitor.sanitized("", maxFractionDigits: 2) == nil,
       "clearing the field must stay allowed")
expect(BoundedTextFieldMonitor.sanitized("3.00", maxFractionDigits: 2) == nil,
       "two fraction digits at the bound must stay untouched")

// 非法内容被就地过滤，而不是拒绝整串或取整回显：
expect(BoundedTextFieldMonitor.sanitized("1.6x", maxFractionDigits: 2) == "1.6",
       "letters must be stripped")
expect(BoundedTextFieldMonitor.sanitized("1.2.3", maxFractionDigits: 2) == "1.23",
       "a second decimal point must be dropped")
expect(BoundedTextFieldMonitor.sanitized("1.234", maxFractionDigits: 2) == "1.23",
       "extra fraction digits must be truncated")
expect(BoundedTextFieldMonitor.sanitized("18.55", maxFractionDigits: 1) == "18.5",
       "margins must keep at most one fraction digit")

// “.” 开头按 0.x 理解，而不是报错：
expect(BoundedTextFieldMonitor.sanitized(".6", maxFractionDigits: 2) == "0.6",
       "leading dot .6 must be completed to 0.6")
expect(BoundedTextFieldMonitor.sanitized(".", maxFractionDigits: 2) == "0.",
       "a lone leading dot must become 0. for continued typing")
expect(BoundedTextFieldMonitor.sanitized(".5", maxFractionDigits: 1) == "0.5",
       "margin .5 must be completed to 0.5")
expect(BoundedTextFieldMonitor.value("0.6") == 0.6, "completed leading-dot value must parse")

// 数值解析（与各窗口失焦/确认校验一致）：
expect(BoundedTextFieldMonitor.value(" 1.60 ") == 1.6, "value must parse with whitespace")
expect(BoundedTextFieldMonitor.value("1,5") == 1.5, "comma decimal separator must parse")
expect(BoundedTextFieldMonitor.value("1.") == 1.0, "trailing point must parse as 1")
expect(BoundedTextFieldMonitor.value("") == nil, "empty must not produce a value")

print("PASS")
