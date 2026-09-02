import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \\(message)\\n", stderr)
        exit(1)
    }
}

expect(EditorHostTransitionPolicy.shouldAnimate(from: "a", to: "b", requested: true, reduceMotion: false),
       "switching between visible tabs animates")
expect(!EditorHostTransitionPolicy.shouldAnimate(from: nil, to: "a", requested: true, reduceMotion: false),
       "initial tab reveal does not animate")
expect(!EditorHostTransitionPolicy.shouldAnimate(from: "a", to: "b", requested: true, reduceMotion: true),
       "reduce motion disables tab transitions")
expect(!EditorHostTransitionPolicy.shouldAnimate(from: "a", to: "a", requested: true, reduceMotion: false),
       "reselecting the active tab does not animate")

print("PASS")
