// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "MarkLeaf",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "MarkLeaf",
            path: "Sources/MarkLeaf"
        ),
        .testTarget(
            name: "MarkLeafTests",
            dependencies: ["MarkLeaf"],
            path: "Tests/MarkLeafTests"
        )
    ]
)
