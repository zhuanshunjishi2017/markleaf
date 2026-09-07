import AppKit
import WebKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

_ = NSApplication.shared
let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let html = """
<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
<style>html,body{margin:0;padding:0}div{height:1200px;background:linear-gradient(red,blue)}</style>
</head><body><div></div></body></html>
"""

func export(_ options: ExportOptions, name: String) -> Result<[URL], Error> {
    var result: Result<[URL], Error>?
    let exporter = ImageHTMLExporter()
    exporter.export(html: html, options: options,
                    saveBaseURL: directory.appendingPathComponent(name)) { result = $0 }
    let deadline = Date().addingTimeInterval(30)
    while result == nil && Date() < deadline {
        RunLoop.main.run(until: Date().addingTimeInterval(0.02))
    }
    expect(result != nil, "export must complete")
    return result!
}

for scale in [1.0, 2.0, 3.0, 4.0] {
    var options = ExportOptions()
    options.format = "image"
    options.imageContentWidth = 320
    options.imageMaxHeight = 1000
    options.imageScale = scale
    let urls = try export(options, name: "scale-\(Int(scale)).png").get()
    let bitmaps = try urls.map { url -> NSBitmapImageRep in
        let data = try Data(contentsOf: url)
        return NSBitmapImageRep(data: data)!
    }
    expect(bitmaps.allSatisfy { $0.pixelsWide == Int(320 * scale) },
           "\(scale)x must produce width \(Int(320 * scale)); got \(bitmaps.map(\.pixelsWide))")
    expect(bitmaps.allSatisfy { $0.pixelsHigh <= 1000 }, "slice height must respect output pixel limit")
    expect(bitmaps.reduce(0) { $0 + $1.pixelsHigh } == Int(1200 * scale), "slices must cover content exactly")
    expect(urls.count == Int(ceil(1200 * scale / 1000)), "slice count must use final pixels")
    print("PASS: \(scale)x \(bitmaps.map { "\($0.pixelsWide)x\($0.pixelsHigh)" })")
}

var jpeg = ExportOptions()
jpeg.format = "image"
jpeg.imageContentWidth = 320
jpeg.imageFormat = "jpg"
jpeg.imageJpegQuality = 67
let jpgURLs = try export(jpeg, name: "quality.jpg").get()
let jpgData = try Data(contentsOf: jpgURLs[0])
expect(Array(jpgData.prefix(2)) == [0xff, 0xd8], "JPG must contain JPEG data")

for badHeight in [0.0, -1, .nan, .infinity, 999, 30001] {
    var options = ExportOptions()
    options.imageMaxHeight = badHeight
    if case .success = export(options, name: "invalid.png") {
        expect(false, "invalid height must fail safely: \(badHeight)")
    }
}
print("PASS: JPEG encoding and invalid input rejection")
