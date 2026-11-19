import Foundation
import CoreGraphics

// 所有逻辑坐标都用 AppKit 约定: 原点在左下, y 向上. 猫的 (x, y) = 脚底中心.

/// 窗口平台和跨屏的可调常量.
public enum PlatformTuning {
    public static let menuBarSafe = 40.0        // 平台顶边离屏幕顶至少这么多点 (每块屏都有菜单栏)
    public static let fullscreenMinCover = 0.8  // 贴屏底、满宽, 且高度过屏高这么多才算全屏
    public static let minHeight = 40.0          // 比这矮的窗口不能站 (仍会挡住后面的窗口)
    public static let crossChance = 0.5         // 想跳的时候, 这个概率允许选别的屏上的目标
    public static let systemBundles: Set<String> = [   // 不算窗口, 既不能站也不挡人
        "com.apple.dock", "com.apple.WindowManager", "com.apple.controlcenter",
        "com.apple.notificationcenterui", "com.apple.systemuiserver", "com.apple.Spotlight",
    ]
}

/// 一段能站的水平线: 窗口顶边或地板.
public struct Platform: Equatable {
    public var lo: Double
    public var hi: Double
    public var top: Double
    public var windowID: Int?       // 窗口编号, 地板是 nil
    public var base: Double?        // 只有地板有: 所在屏底边. [base, top) 里的猫会被托上来
    public init(lo: Double, hi: Double, top: Double, windowID: Int? = nil, base: Double? = nil) {
        self.lo = lo; self.hi = hi; self.top = top; self.windowID = windowID; self.base = base
    }
    public var width: Double { hi - lo }
}

/// CG (左上原点, 向下) 的矩形换成 AppKit (左下原点, 向上).
/// primaryHeight 是主屏高度; CG 全局坐标以主屏左上角为原点, 所以对所有屏都成立.
public func cgToAppKit(_ r: CGRect, primaryHeight: Double) -> CGRect {
    CGRect(x: r.minX, y: primaryHeight - r.maxY, width: r.width, height: r.height)
}

public struct WindowInfo {
    public var rect: CGRect      // 已换成 AppKit 坐标
    public var layer: Int
    public var alpha: Double
    public var pid: Int32
    public var id: Int
    public var bundleID: String?
    public init(rect: CGRect, layer: Int, alpha: Double, pid: Int32, id: Int = 0, bundleID: String? = nil) {
        self.rect = rect; self.layer = layer; self.alpha = alpha; self.pid = pid
        self.id = id; self.bundleID = bundleID
    }
}

public struct ScreenInfo {
    public var frame: CGRect
    public var visible: CGRect
    public init(frame: CGRect, visible: CGRect) { self.frame = frame; self.visible = visible }
}

/// 每块屏一条地板 (可见区底边, Dock 上方), 加上窗口顶边没被挡住的部分.
/// windows 从前到后排 (CGWindowList 的顺序). "显示器具有单独的空间"开着时窗口只显示在
/// 占面积最大的那块屏上, 所以先把窗口裁到那块屏 (实验结论, 见 main.swift Overlay).
public func buildPlatforms(windows: [WindowInfo], ownPID: Int32, screens: [ScreenInfo],
                           catHeight: Double, minWidth: Double) -> [Platform] {
    var out = screens.map { s in
        let full = windows.contains { isFullscreen($0, on: s, ownPID: ownPID) }
        return Platform(lo: Double(s.frame.minX), hi: Double(s.frame.maxX),
                        top: Double(full ? s.frame.minY : s.visible.minY), base: Double(s.frame.minY))
    }
    var front: [CGRect] = []
    for w in windows {
        guard w.layer == 0, w.alpha > 0.05, w.pid != ownPID,
              !PlatformTuning.systemBundles.contains(w.bundleID ?? ""),
              let s = homeScreen(w.rect, screens) else { continue }
        let r = w.rect.intersection(s.frame)
        if isFullscreen(w, on: s, ownPID: ownPID) { front.append(r); continue }   // 全屏窗口顶上不站
        let top = Double(r.maxY)
        let screenTop = Double(s.frame.maxY)
        if Double(r.height) >= PlatformTuning.minHeight, top > Double(s.visible.minY) + 4,
           top + catHeight <= screenTop, top <= screenTop - PlatformTuning.menuBarSafe {
            let hidden = front.filter { $0.minY <= top && top <= $0.maxY }.map { (Double($0.minX), Double($0.maxX)) }
            for seg in subtract(lo: Double(r.minX), hi: Double(r.maxX), hidden) where seg.hi - seg.lo >= minWidth {
                out.append(Platform(lo: seg.lo, hi: seg.hi, top: top, windowID: w.id))
            }
        }
        front.append(r)
    }
    return out
}

/// 窗口铺满整块屏: 这时 Dock 藏着, visibleFrame 却可能还留着它的高度.
/// 刘海屏全屏时顶上留的不止菜单栏 (实测终端留 65 点, 工具栏另成一个窗口), 所以顶边只要求过屏高的 80%.
/// 最大化的窗口停在 Dock 上方 (底边不贴屏底), 不算.
func isFullscreen(_ w: WindowInfo, on s: ScreenInfo, ownPID: Int32) -> Bool {
    guard w.layer == 0, w.alpha > 0.05, w.pid != ownPID,
          !PlatformTuning.systemBundles.contains(w.bundleID ?? "") else { return false }
    return w.rect.minX <= s.frame.minX + 1 && w.rect.maxX >= s.frame.maxX - 1
        && w.rect.minY <= s.frame.minY + 1 && w.rect.minY >= s.frame.minY - 1
        && Double(w.rect.height) >= Double(s.frame.height) * PlatformTuning.fullscreenMinCover
}

/// 和窗口重叠面积最大的屏.
func homeScreen(_ r: CGRect, _ screens: [ScreenInfo]) -> ScreenInfo? {
    func area(_ s: ScreenInfo) -> Double {
        let i = r.intersection(s.frame)
        return i.isNull ? 0 : Double(i.width * i.height)
    }
    guard let best = screens.max(by: { area($0) < area($1) }), area(best) > 0 else { return nil }
    return best
}

/// [lo, hi] 减去若干区间.
func subtract(lo: Double, hi: Double, _ holes: [(Double, Double)]) -> [(lo: Double, hi: Double)] {
    var out: [(lo: Double, hi: Double)] = []
    var cursor = lo
    for h in holes.sorted(by: { $0.0 < $1.0 }) {
        if h.0 > cursor { out.append((cursor, min(h.0, hi))) }
        cursor = max(cursor, h.1)
        if cursor >= hi { return out }
    }
    out.append((cursor, hi))
    return out
}

/// 窗口这一帧相对上一帧要把站在上面的猫带多远. 窗口没了或没动返回 nil.
/// 宽度变了说明是在拖边改大小, 水平不跟.
public func carryDelta(windowID: Int, old: [WindowInfo], new: [WindowInfo]) -> (dx: Double, dy: Double)? {
    guard let a = old.first(where: { $0.id == windowID }), let b = new.first(where: { $0.id == windowID }) else { return nil }
    let dx = a.rect.width == b.rect.width ? Double(b.rect.minX - a.rect.minX) : 0
    let dy = Double(b.rect.maxY - a.rect.maxY)
    return dx == 0 && dy == 0 ? nil : (dx, dy)
}

/// 脚下 (x, y) 是不是还有平台 (顶边在 y 上下 tol 以内).
public func supportAt(_ ps: [Platform], x: Double, y: Double, tol: Double = 3) -> Platform? {
    ps.filter { $0.lo <= x && x <= $0.hi && abs($0.top - y) <= tol }.max { $0.top < $1.top }
}

/// 自由落体一步. vy 向上为正. 返回新 y, 新 vy, 落到哪个平台 (nil = 还在空中).
/// 只会落在"起点不低于顶边、终点不高于顶边"的平台上, 取最高的, 所以不会穿过窗口.
public func fallStep(x: Double, y: Double, vy: Double, dt: Double, g: Double, platforms: [Platform],
                     vx: Double = 0, halfWidth: Double = 0, screens: [CGRect] = [])
    -> (x: Double, y: Double, vy: Double, vx: Double, landed: Platform?) {
    var nvy = vy - g * dt
    var ny = y + nvy * dt
    var nvx = vx
    var nx = x + vx * dt
    if vx != 0 {
        // 前方脚下没有东西会接住 (屏幕外面, 或相邻屏幕的底比猫高): 当成墙弹回来
        let edge = nx + (vx > 0 ? halfWidth : -halfWidth)
        if !platforms.contains(where: { $0.lo <= edge && edge <= $0.hi && $0.top <= y + 1e-6 }) {
            nx = x
            nvx = -vx * ThrowTuning.bounce
        }
    }
    if nvy > 0, !screens.isEmpty, screenIndex(nx, ny, screens) == nil {
        ny = y
        nvy = 0
    }
    let hit = platforms.compactMap { p -> (p: Platform, x: Double)? in
        guard y + 1e-6 >= p.top, ny <= p.top else { return nil }
        let cx = y > ny ? x + (nx - x) * min(max((y - p.top) / (y - ny), 0), 1) : nx    // 穿过这个高度时的横坐标
        return p.lo <= cx && cx <= p.hi ? (p, cx) : nil
    }.max { $0.p.top < $1.p.top }
    if let h = hit { return (h.x, h.p.top, 0, 0, h.p) }
    return (nx, ny, nvy, nvx, nil)
}

/// 一次起跳的抛物线. 顶点只比起点/终点中较高的那个再高 clearance.
public struct Arc {
    public let x0, y0, tx, ty, vy, g, T: Double
    public init(x0: Double, y0: Double, tx: Double, ty: Double, clearance: Double, g: Double) {
        self.x0 = x0; self.y0 = y0; self.tx = tx; self.ty = ty; self.g = g
        let apex = max(y0, ty) + clearance
        vy = (2 * g * (apex - y0)).squareRoot()
        T = vy / g + (2 * (apex - ty) / g).squareRoot()
    }
    public func pos(_ t: Double) -> (x: Double, y: Double) {
        let u = min(max(t / T, 0), 1)
        return (x0 + (tx - x0) * u, y0 + vy * t - g * t * t / 2)
    }
    public func pose(_ t: Double) -> String {
        let u = t / T
        return u < 0.4 ? "rise" : u <= 0.6 ? "apex" : "fall"
    }
}

/// 抛物线上的点是否都在某块屏里. 只有相邻且有重叠的边才过得去, 邻接关系不用单独算.
/// screens 为空表示不限制.
public func arcStaysOnScreens(_ a: Arc, screens: [CGRect]) -> Bool {
    guard !screens.isEmpty else { return true }
    let n = 32
    return (0...n).allSatisfy { i in
        let p = a.pos(a.T * Double(i) / Double(n))
        return screenIndex(p.x, p.y, screens) != nil
    }
}

/// (x, y) 在第几块屏. 留 1 点容差, 脚正好踩在屏的边界上也算.
public func screenIndex(_ x: Double, _ y: Double, _ screens: [CGRect]) -> Int? {
    let p = CGPoint(x: x, y: y)
    return screens.firstIndex { $0.contains(p) } ?? screens.firstIndex { $0.insetBy(dx: -1, dy: -1).contains(p) }
}

/// 选下一个跳点: 不是脚下这块、水平够得着、不太高、allowed 放行. r1/r2 ∈ [0,1) 是随机数 (便于测试).
public func pickJump(from x: Double, y: Double, platforms: [Platform], halfWidth: Double,
                     maxDX: Double, maxRise: Double, r1: Double, r2: Double,
                     allowed: (Platform, Double) -> Bool = { _, _ in true }) -> (platform: Platform, tx: Double)? {
    let cur = supportAt(platforms, x: x, y: y)
    let cands: [(platform: Platform, tx: Double)] = platforms.filter { p in
        p != cur && p.top - y <= maxRise && gap(x, p) <= maxDX
    }.map { p in
        let lo = p.lo + halfWidth, hi = p.hi - halfWidth
        return (p, hi <= lo ? (p.lo + p.hi) / 2 : min(max(x + (r2 - 0.5) * 240, lo), hi))
    }.filter { allowed($0.platform, $0.tx) }
    guard !cands.isEmpty else { return nil }
    return cands[min(Int(r1 * Double(cands.count)), cands.count - 1)]
}

func gap(_ x: Double, _ p: Platform) -> Double { x < p.lo ? p.lo - x : x > p.hi ? x - p.hi : 0 }
