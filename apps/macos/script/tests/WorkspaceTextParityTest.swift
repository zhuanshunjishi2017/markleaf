import Foundation

struct Fixture: Decodable {
    struct FileCase: Decodable { let name: String; let included: Bool }
    struct Projection: Decodable { let name: String; let source: String; let isMarkdown: Bool; let expected: String }
    let files: [FileCase]
    let projections: [Projection]
}

let fixture = try JSONDecoder().decode(Fixture.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
var failures = 0
func expect(_ condition: Bool, _ name: String) {
    if !condition { fputs("FAIL: \(name)\n", stderr); failures += 1 }
}
for item in fixture.projections {
    expect(MarkdownPlainText.fromDocument(item.source, isMarkdown: item.isMarkdown) == item.expected, item.name)
}
let root = FileManager.default.temporaryDirectory.appendingPathComponent("markleaf-text-parity-\(UUID().uuidString)")
try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: root) }
for item in fixture.files { try "content".write(to: root.appendingPathComponent(item.name), atomically: true, encoding: .utf8) }
let expected = Set(fixture.files.filter(\.included).map(\.name))
var treeNames: Set<String>?
var documentNames: Set<String>?
let scanner = WorkspaceScanner(root: root.path) { treeNames = Set($0.map(\.name)) }
scanner.scan()
scanner.scanDocuments { documentNames = Set($0.map(\.name)) }
let deadline = Date().addingTimeInterval(3)
while (treeNames == nil || documentNames == nil), Date() < deadline {
    RunLoop.main.run(until: Date().addingTimeInterval(0.01))
}
expect(treeNames == expected, "tree uses Windows text file scope")
expect(documentNames == expected, "document list uses Windows text file scope")
guard failures == 0 else { exit(1) }
print("PASS: \(fixture.projections.count) shared text projections and tree/list file scope")
