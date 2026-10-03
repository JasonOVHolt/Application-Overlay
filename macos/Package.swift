// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "WindowOverlay",
    platforms: [.macOS(.v13)], // ScreenCaptureKit window capture + SwiftUI Window scene
    targets: [
        .executableTarget(
            name: "WindowOverlay",
            path: "Sources/WindowOverlay"
        ),
        .testTarget(
            name: "WindowOverlayTests",
            dependencies: ["WindowOverlay"],
            path: "Tests/WindowOverlayTests"
        ),
    ]
)
