import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(SampleDocumentResource.allCases.count == 3, "Windows 1.6.0 needs three bundled sample documents")
expect(SampleDocumentResource.allCases.map(\.fileName) == [
    "alert-examples.md",
    "yaml-front-matter-basic.md",
    "yaml-front-matter-advanced.md",
], "sample resource names must match the migration contract")
expect(SampleDocumentResource(command: "openSampleAlert") == .alertExamples, "alert menu command should map to its sample")
expect(SampleDocumentResource(command: "openSampleYamlBasic") == .yamlBasic, "basic YAML menu command should map to its sample")
expect(SampleDocumentResource(command: "openSampleYamlAdvanced") == .yamlAdvanced, "advanced YAML menu command should map to its sample")
expect(SampleDocumentResource(command: "openUnknown") == nil, "unknown commands must not resolve to samples")

let cache = URL(fileURLWithPath: "/tmp/markleaf-sample-cache", isDirectory: true)
for sample in SampleDocumentResource.allCases {
    expect(sample.cachedURL(cacheDirectory: cache).lastPathComponent == sample.fileName,
           "each editable sample cache copy should preserve its file name")
}

let fixtureRoot = FileManager.default.temporaryDirectory
    .appendingPathComponent("markleaf-sample-resource-\(UUID().uuidString).bundle", isDirectory: true)
let resources = fixtureRoot.appendingPathComponent("Contents/Resources/Samples", isDirectory: true)
try FileManager.default.createDirectory(at: resources, withIntermediateDirectories: true)
try """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.markleaf.SampleResourceTest</string>
<key>CFBundlePackageType</key><string>BNDL</string>
</dict></plist>
""".write(to: fixtureRoot.appendingPathComponent("Contents/Info.plist"), atomically: true, encoding: .utf8)
try "sample".write(to: resources.appendingPathComponent("alert-examples.md"), atomically: true, encoding: .utf8)
defer { try? FileManager.default.removeItem(at: fixtureRoot) }

let fixtureBundle = Bundle(url: fixtureRoot)!
expect(SampleDocumentResource.alertExamples.bundledURL(in: fixtureBundle)?.lastPathComponent == "alert-examples.md",
       "samples should resolve from the bundle Samples directory")

print("PASS")
