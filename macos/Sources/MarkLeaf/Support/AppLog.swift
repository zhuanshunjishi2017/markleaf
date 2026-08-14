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

    /// 清理超过指定天数的旧日志文件（对齐 Windows 1.1.3 CleanOldLogs）。
    /// 文件缺失或日期属性不可读时静默忽略。
    static func cleanup(fileURL: URL, olderThanDays days: Int, now: Date = Date()) {
        guard days > 0,
              let attributes = try? FileManager.default.attributesOfItem(atPath: fileURL.path),
              let modified = attributes[.modificationDate] as? Date else { return }
        let cutoff = Calendar.current.date(byAdding: .day, value: -days, to: now) ?? now
        if modified < cutoff {
            try? FileManager.default.removeItem(at: fileURL)
        }
    }

    /// 清理默认日志文件（对齐 Windows 1.1.3 CleanOldLogs）。
    static func cleanupOldLogs(olderThanDays days: Int, now: Date = Date()) {
        cleanup(fileURL: AppLog.fileURL, olderThanDays: days, now: now)
    }
}
