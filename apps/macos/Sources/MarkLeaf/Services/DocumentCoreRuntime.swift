import Foundation
import CoreFoundation
import JavaScriptCore

struct DocumentKernelFailure: LocalizedError {
    let code: String
    let message: String
    var errorDescription: String? { message }
}

/// Runtime/codec adapter. All document decisions are made by editor-core's DOM-free bundle.
final class DocumentCoreRuntime {
    static let shared = DocumentCoreRuntime()
    private let lock = NSLock()
    private var context: JSContext?

    func call<T: Decodable>(_ method: String, _ payload: [String: Any] = [:], as: T.Type = T.self) throws -> T {
        lock.lock()
        defer { lock.unlock() }
        let runtime = try loadRuntime()
        let request = try JSONSerialization.data(withJSONObject: ["method": method, "payload": payload])
        runtime.exception = nil
        guard let result = runtime.objectForKeyedSubscript("MarkLeafDocumentCore")?
            .invokeMethod("invoke", withArguments: [String(decoding: request, as: UTF8.self)])?.toString(),
              runtime.exception == nil else {
            throw DocumentKernelFailure(code: "runtime_error", message: runtime.exception?.toString() ?? "Document kernel returned no result")
        }
        let data = Data(result.utf8)
        let envelope = try JSONDecoder().decode(KernelEnvelope.self, from: data)
        guard envelope.ok else {
            throw DocumentKernelFailure(code: envelope.error?.code ?? "invalid_response", message: envelope.error?.message ?? "Document kernel returned no value")
        }
        let object = try JSONSerialization.jsonObject(with: data) as! [String: Any]
        let value = try JSONSerialization.data(withJSONObject: object["value"] ?? NSNull(), options: [.fragmentsAllowed])
        return try JSONDecoder().decode(T.self, from: value)
    }

    // Infallible policy queries must never silently substitute a second implementation.
    func require<T: Decodable>(_ method: String, _ payload: [String: Any] = [:], as: T.Type = T.self) -> T {
        do { return try call(method, payload, as: T.self) }
        catch { preconditionFailure("Document kernel contract failed: \(error.localizedDescription)") }
    }

    func validateSave(_ payload: [String: Any]) throws {
        let _: Bool = try call("validateSave", payload)
    }

    private func loadRuntime() throws -> JSContext {
        if let context { return context }
        guard let url = Bundle.main.url(forResource: "document-kernel", withExtension: "cjs", subdirectory: "DocumentCore"),
              let runtime = JSContext() else {
            throw DocumentKernelFailure(code: "runtime_unavailable", message: "The packaged document kernel is missing")
        }
        let codec: @convention(block) (String) -> String = Self.convert
        runtime.setObject(codec, forKeyedSubscript: "__markleafDocumentCodec" as NSString)
        runtime.evaluateScript(try String(contentsOf: url, encoding: .utf8), withSourceURL: url)
        if let exception = runtime.exception { throw DocumentKernelFailure(code: "runtime_error", message: exception.toString()) }
        context = runtime
        return runtime
    }

    private static func convert(_ request: String) -> String {
        guard let input = try? JSONSerialization.jsonObject(with: Data(request.utf8)) as? [String: Any],
              let name = input["codec"] as? String else { return "null" }
        let cfEncoding = CFStringConvertIANACharSetNameToEncoding(name as CFString)
        guard cfEncoding != kCFStringEncodingInvalidId else { return "null" }
        let encoding = String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(cfEncoding))
        let value: Any
        if input["operation"] as? String == "decode" {
            guard let bytes = input["bytes"] as? [UInt8], let text = String(data: Data(bytes), encoding: encoding) else { return "null" }
            value = text
        } else {
            guard let text = input["text"] as? String, let bytes = text.data(using: encoding, allowLossyConversion: false) else { return "null" }
            value = Array(bytes)
        }
        guard let data = try? JSONSerialization.data(withJSONObject: value, options: [.fragmentsAllowed]) else { return "null" }
        return String(decoding: data, as: UTF8.self)
    }
}

private struct KernelEnvelope: Decodable {
    let ok: Bool
    let error: KernelError?
    struct KernelError: Decodable { let code: String; let message: String }
}

struct KernelEncoding: Decodable {
    let id: String
    let label: String
    let codec: String
    let codePage: Int
    let bom: [UInt8]
}
struct KernelDocument: Decodable { let text: String; let encoding: KernelEncoding; let newLine: String }
struct KernelPreparedSave: Decodable { let text: String; let bytes: [UInt8]; let encoding: KernelEncoding }
struct KernelSearchMatch: Decodable { let matched: Bool; let isContentMatch: Bool; let snippet: String }
struct KernelRecovery: Codable {
    let documentId: String
    let documentPath: String?
    let markdown: String
    let revision: String
    let timestamp: String
    let displayName: String?
    let encoding: String?
    let newLine: String?
}
