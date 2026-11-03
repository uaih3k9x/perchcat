import Foundation
@testable import PerchCatCore

// 这里单独放用到 Foundation 的辅助函数: 同一个文件同时 import Testing 和 Foundation
// 会要求 _Testing_Foundation, CommandLineTools 里没有.

func notJSON() -> Data { Data("not json".utf8) }

func sheetJSON(stages: [[String: Any]], grip: [Int] = [11, 3]) -> Data {
    try! JSONSerialization.data(withJSONObject: [
        "palette": ["K": "#000000"], "grip": grip, "headMidCol": 12, "stages": stages,
    ] as [String: Any])
}

/// 只用临时目录, 绝不碰真的 ~/Library/LaunchAgents.
func withTempAgent(_ body: (LaunchAgent) throws -> Void) rethrows {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent("perchcat-test-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: dir) }
    try body(LaunchAgent(plistURL: dir.appendingPathComponent("sub/\(LaunchAgent.label).plist")))
}

func readPlist(_ a: LaunchAgent) throws -> [String: Any]? {
    try PropertyListSerialization.propertyList(from: Data(contentsOf: a.plistURL), format: nil) as? [String: Any]
}

func shakeJSON(pad: Int, width: Int, height: Int, frames: [String]) -> Data {
    try! JSONSerialization.data(withJSONObject: ["pad": pad, "width": width, "height": height, "frames": frames] as [String: Any])
}

/// 用户自己的皮肤位置 (不在仓库里, 可能不存在).
func realSkinPath() -> String { defaultSkinPath(home: NSHomeDirectory()) }

extension String { var utf8Data: Data { Data(utf8) } }
