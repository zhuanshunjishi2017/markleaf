import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let fileManager = FileManager.default
let temporaryDirectory = fileManager.temporaryDirectory
    .appendingPathComponent("markleaf-encoding-conversion-\(UUID().uuidString)", isDirectory: true)
try fileManager.createDirectory(at: temporaryDirectory, withIntermediateDirectories: true)
defer { try? fileManager.removeItem(at: temporaryDirectory) }

let directReadURL = temporaryDirectory.appendingPathComponent("direct-read.md")
let directReadBytes = DocumentEncodingPolicy.encode("磁盘内容", using: .utf8)!
try directReadBytes.write(to: directReadURL)
let fixedModificationDate = Date(timeIntervalSince1970: 1_700_000_000)
try fileManager.setAttributes([.modificationDate: fixedModificationDate], ofItemAtPath: directReadURL.path)
let modificationDateBeforeRead = try directReadURL.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate

let directlyReadSnapshot = try EncodingConversionTransaction.directlyRead(
    from: directReadURL,
    using: .utf8
)
let directReadBytesAfterRead = try Data(contentsOf: directReadURL)
let modificationDateAfterRead = try directReadURL.resourceValues(
    forKeys: [.contentModificationDateKey]
).contentModificationDate

expect(directlyReadSnapshot.markdown == "磁盘内容", "direct read should decode the current disk bytes")
expect(directlyReadSnapshot.data == directReadBytes, "direct read should expose the exact bytes it decoded")
expect(directReadBytesAfterRead == directReadBytes,
       "direct read must not change the source file bytes")
expect(modificationDateAfterRead == modificationDateBeforeRead,
       "direct read must not change the source file modification time")

let shiftJISData = try EncodingConversionTransaction.encodedData(
    markdown: "日本語テキスト",
    target: .shiftJIS
)
expect(DocumentEncodingPolicy.decode(shiftJISData, using: .shiftJIS) == "日本語テキスト",
       "conversion data should round-trip in the target encoding")

do {
    _ = try EncodingConversionTransaction.encodedData(markdown: "中文", target: .usASCII)
    expect(false, "unrepresentable text should throw instead of being converted lossily")
} catch EncodingConversionTransactionError.unrepresentableText {
    // Expected.
}

let failedWriteURL = temporaryDirectory.appendingPathComponent("failed-write.md")
let failedWriteOriginalBytes = DocumentEncodingPolicy.encode("原始文件", using: .utf8)!
try failedWriteOriginalBytes.write(to: failedWriteURL)
let dirtySnapshot = EncodingConversionSnapshot(markdown: "尚未保存的中文", encoding: .utf8, isDirty: true)

do {
    _ = try EncodingConversionTransaction.convert(
        snapshot: dirtySnapshot,
        target: .gb18030,
        destination: failedWriteURL,
        atomicWriter: { _, _ in throw CocoaError(.fileWriteNoPermission) }
    )
    expect(false, "a failed atomic write should throw")
} catch {
    let failedWriteBytesAfterAttempt = try Data(contentsOf: failedWriteURL)
    expect(failedWriteBytesAfterAttempt == failedWriteOriginalBytes,
           "a failed conversion must leave the original file bytes intact")
    expect(dirtySnapshot.encoding == .utf8 && dirtySnapshot.isDirty,
           "a failed conversion must leave the caller snapshot unchanged")
}

let successfulWriteURL = temporaryDirectory.appendingPathComponent("successful-write.md")
try DocumentEncodingPolicy.encode("旧内容", using: .utf8)!.write(to: successfulWriteURL)
let successfulSnapshot = try EncodingConversionTransaction.convert(
    snapshot: dirtySnapshot,
    target: .gb18030,
    destination: successfulWriteURL
)
let successfulWriteBytes = try Data(contentsOf: successfulWriteURL)

expect(DocumentEncodingPolicy.decode(successfulWriteBytes, using: .gb18030) == dirtySnapshot.markdown,
       "successful conversion should write the current editor Markdown in the target encoding")
expect(successfulSnapshot == EncodingConversionSnapshot(
    markdown: dirtySnapshot.markdown,
    encoding: .gb18030,
    isDirty: false
), "successful conversion should publish the new encoding and clean state only after writing")

print("PASS")
