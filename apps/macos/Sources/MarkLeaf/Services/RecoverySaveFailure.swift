import Foundation
import Darwin

/// 恢复写回原文件时的失败分类（按底层 POSIX errno）。
enum RecoverySaveFailure: Equatable {
    case fileMissing
    case unreachableVolume
    case readOnly
    case diskFull
    case other

    static func classify(error: Error) -> RecoverySaveFailure {
        switch posixCode(of: error) {
        case ENOENT: return .fileMissing
        case ENXIO, ESTALE, EIO, ENOTCONN: return .unreachableVolume
        case EACCES, EPERM, EROFS: return .readOnly
        case ENOSPC: return .diskFull
        default: return .other
        }
    }

    private static func posixCode(of error: Error) -> Int32? {
        let nsError = error as NSError
        if nsError.domain == NSPOSIXErrorDomain { return Int32(nsError.code) }
        for underlying in nsError.underlyingErrors {
            let wrapped = underlying as NSError
            if wrapped.domain == NSPOSIXErrorDomain { return Int32(wrapped.code) }
        }
        return nil
    }
}
