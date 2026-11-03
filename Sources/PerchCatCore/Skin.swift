import Foundation

public struct SkinError: Error, CustomStringConvertible {
    public let description: String
}

/// 像素皮肤: 字符调色板 + 命名姿势 (每个姿势是等宽字符行, "." 透明).
public struct Skin {
    public let palette: [Character: (r: UInt8, g: UInt8, b: UInt8)]
    public let poses: [String: [String]]
    public let width: Int
    public let height: Int
    /// 脚底到图片底边隔了几行 (按 stand 算), 用来把脚对齐平台线.
    public let footInset: Int

    public static func load(path: String) throws -> Skin {
        guard let data = FileManager.default.contents(atPath: path) else {
            throw SkinError(description: "读不到皮肤文件: \(path)\n用 --skin 路径 或环境变量 PERCHCAT_SKIN 指定。")
        }
        return try parse(data, source: path)
    }

    public static func parse(json: String) throws -> Skin { try parse(Data(json.utf8)) }

    public static func parse(_ data: Data, source: String = "皮肤") throws -> Skin {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let pal = obj["palette"] as? [String: String],
              let poses = obj["poses"] as? [String: [String]] else {
            throw SkinError(description: "\(source) 不是合法皮肤: 需要 {\"palette\":{...},\"poses\":{...}}")
        }
        let palette = try parsePalette(pal, source: source)
        guard let stand = poses["stand"], let w = stand.first?.count, w > 0 else {
            throw SkinError(description: "\(source) 缺少 stand 姿势")
        }
        for (name, rows) in poses where rows.count != stand.count || rows.contains(where: { $0.count != w }) {
            throw SkinError(description: "\(source) 姿势 \(name) 尺寸与 stand 不一致")
        }
        let lastInk = stand.lastIndex { $0.contains { $0 != "." } } ?? stand.count - 1
        return Skin(palette: palette, poses: poses, width: w, height: stand.count,
                    footInset: stand.count - 1 - lastInk)
    }

    /// "walk3@half" -> 有就用, 没有退回 "walk3", 再退回 stand.
    public func resolve(_ frame: String) -> [String] {
        if let p = poses[frame] { return p }
        if ShakeSheet.index(of: frame) != nil { return poses["sit@happy"] ?? poses["sit"] ?? poses["stand"]! }
        if let base = frame.split(separator: "@").first, let p = poses[String(base)] { return p }
        return poses["stand"]!
    }

    /// RGBA8 (非预乘之前的直通色, 不透明像素 alpha=255).
    public func rgba(_ rows: [String]) -> [UInt8] { rgbaPixels(rows, width: width, palette: palette) }
}

/// 不指定 --skin 时读这里. 皮肤和同目录的 pet-scruff.json, pet-shake.json 都不随代码发布.
public func defaultSkinPath(home: String) -> String {
    home + "/Library/Application Support/perchcat/pet-skin.json"
}

/// 用哪份皮肤: 明确指定的优先 (写错了要报错); 否则默认目录有就用; 都没有返回 nil, 用内置橘猫.
public func chooseSkin(explicit: String?, defaultPath: String, exists: (String) -> Bool) -> String? {
    if let explicit { return explicit }
    return exists(defaultPath) ? defaultPath : nil
}

/// 抖毛帧 (pet-shake.json, 和皮肤放一起): 带半透明拖影和括号线, 所以是整张 RGBA, 四周比猫多 pad 格.
public struct ShakeSheet {
    public let pad: Int
    public let width: Int
    public let height: Int
    public let frames: [[UInt8]]        // 预乘 alpha 的 RGBA

    public static func path(besideSkin skin: String) -> String {
        (skin as NSString).deletingLastPathComponent + "/pet-shake.json"
    }

    /// 文件不存在返回 nil (退回眯眼坐着); 存在但坏了报错.
    public static func load(path: String) throws -> ShakeSheet? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        return try parse(data)
    }

    public static func parse(_ data: Data) throws -> ShakeSheet {
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let pad = o["pad"] as? Int, let w = o["width"] as? Int, let h = o["height"] as? Int,
              let hexes = o["frames"] as? [String], !hexes.isEmpty else {
            throw SkinError(description: "抖毛表不是合法 JSON")
        }
        let frames = try hexes.map { hex -> [UInt8] in
            guard hex.count == w * h * 8, var px = bytes(hex) else { throw SkinError(description: "抖毛帧尺寸不对") }
            for i in stride(from: 0, to: px.count, by: 4) {
                let a = Int(px[i + 3])
                for c in 0..<3 { px[i + c] = UInt8(Int(px[i + c]) * a / 255) }
            }
            return px
        }
        return ShakeSheet(pad: pad, width: w, height: h, frames: frames)
    }

    /// "shake1" -> 1; 不是抖毛帧 -> nil.
    public static func index(of frame: String) -> Int? {
        frame.hasPrefix("shake") ? Int(frame.dropFirst(5)) : nil
    }

    private static func bytes(_ hex: String) -> [UInt8]? {
        var out: [UInt8] = []
        out.reserveCapacity(hex.utf8.count / 2)
        var hi: UInt8?
        for c in hex.utf8 {
            guard let v = Character(UnicodeScalar(c)).hexDigitValue else { return nil }
            if let h = hi { out.append(h * 16 + UInt8(v)); hi = nil } else { hi = UInt8(v) }
        }
        return out
    }
}

func parsePalette(_ pal: [String: String], source: String) throws -> [Character: (r: UInt8, g: UInt8, b: UInt8)] {
    var palette: [Character: (r: UInt8, g: UInt8, b: UInt8)] = [:]
    for (k, v) in pal {
        guard k.count == 1, let c = k.first, let rgb = hex(v) else {
            throw SkinError(description: "\(source) 调色板项无效: \(k) = \(v)")
        }
        palette[c] = rgb
    }
    return palette
}

func rgbaPixels(_ rows: [String], width: Int, palette: [Character: (r: UInt8, g: UInt8, b: UInt8)]) -> [UInt8] {
    var buf = [UInt8](repeating: 0, count: width * rows.count * 4)
    for (y, row) in rows.enumerated() {
        for (x, ch) in row.enumerated() {
            guard let c = palette[ch] else { continue }
            let i = (y * width + x) * 4
            buf[i] = c.r; buf[i + 1] = c.g; buf[i + 2] = c.b; buf[i + 3] = 255
        }
    }
    return buf
}

func hex(_ s: String) -> (UInt8, UInt8, UInt8)? {
    var t = s
    if t.hasPrefix("#") { t.removeFirst() }
    guard t.count == 6, let v = UInt32(t, radix: 16) else { return nil }
    return (UInt8(v >> 16 & 255), UInt8(v >> 8 & 255), UInt8(v & 255))
}
