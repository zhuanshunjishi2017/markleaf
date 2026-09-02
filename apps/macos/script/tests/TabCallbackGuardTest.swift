import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let guard_ = TabCallbackGuard()
let tabA = DocumentTabID()
let tabB = DocumentTabID()

let tokenA1 = guard_.token(for: tabA)
expect(guard_.shouldApply(tokenA1), "a fresh token should apply")

guard_.invalidate(tabA)
expect(!guard_.shouldApply(tokenA1), "a token from before invalidation should be discarded")

let tokenA2 = guard_.token(for: tabA)
expect(guard_.shouldApply(tokenA2), "a token issued after invalidation should apply")

let tokenB = guard_.token(for: tabB)
guard_.remove(tabB)
expect(!guard_.shouldApply(tokenB), "tokens of removed tabs should never apply")

expect(!guard_.shouldApply(AsyncCallbackToken(tabID: DocumentTabID(), generation: 0)),
       "tokens of unknown tabs should not apply")
print("PASS")
