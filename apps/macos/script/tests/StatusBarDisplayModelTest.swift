import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let model = StatusBarDisplayModel(
    commandStatus: "已保存",
    blockType: "正文",
    line: 3,
    column: 4,
    characterCount: 12,
    encoding: "UTF-8",
    newLine: "LF",
    mode: "可视化",
    zoomPercent: 100
)
expect(model.commandStatus == "已保存", "command status must be independent")
expect(model.characterCount == 12 && model.line == 3 && model.column == 4,
       "document metrics must be independent fields")
expect(StatusBarDisplayPolicy.shouldShowCommandStatus(
    commandStatus: "缩放 161%",
    zoomVisible: true,
    zoomStatus: "缩放 161%"
) == false, "duplicate zoom feedback should be hidden")
expect(StatusBarDisplayPolicy.shouldShowCommandStatus(
    commandStatus: "已保存",
    zoomVisible: true,
    zoomStatus: "缩放 161%"
) == true, "non-zoom command feedback should remain visible")
expect(StatusBarDisplayPolicy.shouldShowCommandStatus(
    commandStatus: "缩放 161%",
    zoomVisible: false,
    zoomStatus: "缩放 161%"
) == true, "zoom feedback should remain visible when zoom field is hidden")

// 状态栏模式按钮必须按当前可视/源码状态显示对应文案，不能再统一渲染成 </>。
expect(StatusBarModePolicy.title(isSourceMode: true) == "源码",
       "source mode should display the source label")
expect(StatusBarModePolicy.title(isSourceMode: false) == "可视化",
       "visual mode should display the visual label")
print("PASS")
