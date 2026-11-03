// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "perchcat",
    platforms: [.macOS(.v13)],
    targets: [
        .target(name: "PerchCatCore"),
        .executableTarget(name: "perchcat", dependencies: ["PerchCatCore"]),
        .testTarget(name: "PerchCatCoreTests", dependencies: ["PerchCatCore"]),
    ]
)
