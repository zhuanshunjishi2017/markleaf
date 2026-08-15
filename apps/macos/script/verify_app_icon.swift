import AppKit
import Foundation

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data(("[icon] " + message + "\n").utf8))
    exit(1)
}

guard CommandLine.arguments.count == 2 else { fail("usage: verify_app_icon.swift <png>") }
let path = CommandLine.arguments[1]
guard let data = try? Data(contentsOf: URL(fileURLWithPath: path)),
      let rep = NSBitmapImageRep(data: data) else { fail("cannot decode PNG") }
guard rep.pixelsWide == 1024, rep.pixelsHigh == 1024 else { fail("expected 1024x1024") }
for point in [(0, 0), (1023, 0), (0, 1023), (1023, 1023)] {
    guard (rep.colorAt(x: point.0, y: point.1)?.alphaComponent ?? 1) < 0.01 else {
        fail("corner is not transparent: \(point)")
    }
}
var minX = 1024, minY = 1024, maxX = -1, maxY = -1
for y in 0..<1024 {
    for x in 0..<1024 where (rep.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 64.0 / 255.0 {
        minX = min(minX, x); minY = min(minY, y)
        maxX = max(maxX, x); maxY = max(maxY, y)
    }
}
guard maxX >= minX, maxY >= minY else { fail("no strong-alpha pixels") }
let widthRatio = Double(maxX - minX + 1) / 1024.0
let heightRatio = Double(maxY - minY + 1) / 1024.0
print("[icon] bbox=(\(minX),\(minY))-(\(maxX),\(maxY)) ratio=\(widthRatio)x\(heightRatio)")
guard (0.81...0.82).contains(widthRatio), (0.81...0.82).contains(heightRatio) else {
    fail("strong-alpha bounds outside 81%-82%")
}
