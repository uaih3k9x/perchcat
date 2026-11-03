import Foundation

/// 开机自启: ~/Library/LaunchAgents 下一个 plist. RunAtLoad, 不设 KeepAlive, 退出后不会自己重启.
/// 登录时 launchd 才读它, 所以这里只写文件, 不调 launchctl.
public struct LaunchAgent {
    public static let label = "local.perchcat"

    public static var defaultURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }

    public static func plist(executable: String, skinPath: String?) -> [String: Any] {
        let args = skinPath.map { [executable, "--skin", $0] } ?? [executable]
        return ["Label": label, "RunAtLoad": true, "ProgramArguments": args]
    }

    public let plistURL: URL

    public init(plistURL: URL = LaunchAgent.defaultURL) { self.plistURL = plistURL }

    public var isInstalled: Bool { FileManager.default.fileExists(atPath: plistURL.path) }

    public func install(executable: String, skinPath: String?) throws {
        let data = try PropertyListSerialization.data(fromPropertyList: Self.plist(executable: executable, skinPath: skinPath),
                                                      format: .xml, options: 0)
        try FileManager.default.createDirectory(at: plistURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: plistURL, options: .atomic)
    }

    public func remove() throws {
        guard isInstalled else { return }
        try FileManager.default.removeItem(at: plistURL)
    }
}
