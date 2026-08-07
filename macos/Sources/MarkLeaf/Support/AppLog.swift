import Foundation
import os

/// 统一日志：同时写入 os.Logger（统一日志）与 /tmp/markleaf-app.log（文件日志），
/// 保证在任何环境下都可可靠读取。
enum AppLog {
    static let subsystem = "com.markleaf.app"
    static let logger = Logger(subsystem: subsystem, category: "markleaf")

    private static let fileURL = URL(fileURLWithPath: "/tmp/markleaf-app.log")
    private static let lock = NSLock()

    private static func appendToFile(_ level: String, _ message: String) {
        lock.lock()
        defer { lock.unlock() }
        let line = "\(ISO8601DateFormatter().string(from: Date())) [\(level)] \(message)\n"
        if let handle = try? FileHandle(forWritingTo: fileURL) {
            handle.seekToEndOfFile()
            handle.write(Data(line.utf8))
            try? handle.close()
        } else {
            try? Data(line.utf8).write(to: fileURL, options: .atomic)
        }
    }

    static func info(_ message: String) {
        logger.info("\(message, privacy: .public)")
        appendToFile("INFO", message)
    }

    static func warning(_ message: String) {
        logger.warning("\(message, privacy: .public)")
        appendToFile("WARN", message)
    }

    static func error(_ message: String) {
        logger.error("\(message, privacy: .public)")
        appendToFile("ERROR", message)
    }
}
