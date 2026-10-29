// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "LoadRings",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "LoadRings",
            path: "Sources/LoadRings"
        )
    ]
)
