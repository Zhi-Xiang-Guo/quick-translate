// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "QuickTranslateCore",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "QuickTranslateCore", targets: ["QuickTranslateCore"]),
        .executable(name: "QuickTranslateCoreTests", targets: ["QuickTranslateCoreTests"])
    ],
    targets: [
        .target(name: "QuickTranslateCore"),
        .executableTarget(
            name: "QuickTranslateCoreTests",
            dependencies: ["QuickTranslateCore"],
            path: "Tests/QuickTranslateCoreTests"
        )
    ]
)
