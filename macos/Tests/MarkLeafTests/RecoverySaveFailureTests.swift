import XCTest
@testable import MarkLeaf

final class RecoverySaveFailureTests: XCTestCase {
    func testClassifiesFileMissing() {
        XCTAssertEqual(
            RecoverySaveFailure.classify(error: NSError(domain: NSPOSIXErrorDomain, code: Int(ENOENT))),
            .fileMissing
        )
    }

    func testClassifiesUnreachableVolume() {
        for code in [ENXIO, ESTALE, EIO, ENOTCONN] {
            XCTAssertEqual(
                RecoverySaveFailure.classify(error: NSError(domain: NSPOSIXErrorDomain, code: Int(code))),
                .unreachableVolume
            )
        }
    }

    func testClassifiesReadOnly() {
        for code in [EACCES, EPERM, EROFS] {
            XCTAssertEqual(
                RecoverySaveFailure.classify(error: NSError(domain: NSPOSIXErrorDomain, code: Int(code))),
                .readOnly
            )
        }
    }

    func testClassifiesDiskFull() {
        XCTAssertEqual(
            RecoverySaveFailure.classify(error: NSError(domain: NSPOSIXErrorDomain, code: Int(ENOSPC))),
            .diskFull
        )
    }

    func testClassifiesUnknownAsOther() {
        XCTAssertEqual(
            RecoverySaveFailure.classify(error: NSError(domain: NSCocoaErrorDomain, code: 999)),
            .other
        )
    }
}
