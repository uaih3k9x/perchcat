import Foundation
import CoreGraphics

/// 拎猫的阈值. 时间单位秒, 速度单位"皮肤格/秒".
public enum LiftTuning {
    public static let liftUpPoints = 16.0       // 按住往上提够这么多点才拎起
    public static let liftUpRatio = 1.5         // 上提距离至少是横向的这么多倍, 否则算蹭
    public static let droopAfter = 1.5          // 刚拎起在第一阶段停多久
    public static let droopStep = 0.45          // 之后每隔这么久往下坠一节
    public static let swayBeat = 0.125          // 摆动每帧多久
    public static let dragCellsPerSec = 20.0    // 比这快才用大摆
    public static let fastCellsPerSec = 100.0   // 比这快摆得更开
    public static let lagHold = 0.25            // 手停了之后大摆再撑多久
    public static let fallbackGrip = (col: 17, row: 9)   // 没有拎起帧时拎 fall 姿势的头
}

/// 甩猫的阈值. 速度单位: 鼠标是点/秒, 上限是皮肤格/秒.
public enum ThrowTuning {
    public static let window = 0.1              // 只看松手前这么久的鼠标
    public static let idle = 0.06               // 松手前停了这么久就算没甩
    public static let minMouseSpeed = 150.0     // 比这慢就是直接放下
    public static let gain = 0.7                // 鼠标速度折算成猫的速度
    public static let maxSideCells = 400.0
    public static let maxUpCells = 225.0
    public static let bounce = 0.4              // 撞屏幕边后留下的速度
}

/// 记下拖动轨迹, 松手时算甩出去的速度.
public struct ThrowTracker {
    private var samples: [(x: Double, y: Double, t: Double)] = []

    public init() {}

    public mutating func add(x: Double, y: Double, at t: Double) {
        samples.append((x, y, t))
        samples.removeAll { $0.t < t - ThrowTuning.window - 1e-9 }
    }

    public func velocity(at now: Double) -> (x: Double, y: Double) {
        guard let last = samples.last, let first = samples.first, last.t > first.t,
              now - last.t <= ThrowTuning.idle else { return (0, 0) }
        let dt = last.t - first.t
        return ((last.x - first.x) / dt, (last.y - first.y) / dt)
    }
}

/// 鼠标速度折算成猫飞出去的初速度: 太慢算放下, 横向和向上封顶, 向下不封.
public func throwVelocity(mouseVX: Double, mouseVY: Double, cell: Double) -> (vx: Double, vy: Double) {
    guard hypot(mouseVX, mouseVY) >= ThrowTuning.minMouseSpeed else { return (0, 0) }
    let side = ThrowTuning.maxSideCells * cell
    return (min(max(mouseVX * ThrowTuning.gain, -side), side),
            min(mouseVY * ThrowTuning.gain, ThrowTuning.maxUpCells * cell))
}

/// 把猫身上的一次按下分成: 点一下、按着蹭、往上提. 只有在猫窗口收到 mouseDown 之后才算数.
public struct PressGesture {
    public enum Action: Equatable { case none, lift, move, rub, click, drop }

    private var start: CGPoint?
    private var anchor = CGPoint.zero           // 这一段上提从哪开始; 往下或往旁边挪就重新算
    private var last = CGPoint.zero
    private var lifted = false

    public init() {}

    public var isPressed: Bool { start != nil }

    public mutating func down(_ p: CGPoint) { start = p; anchor = p; last = p; lifted = false }

    public mutating func moved(_ p: CGPoint) -> Action {
        guard start != nil else { return .none }
        if lifted { return .move }
        let step = hypot(p.x - last.x, p.y - last.y)
        last = p
        let up = p.y - anchor.y, side = abs(p.x - anchor.x)
        if up < side * LiftTuning.liftUpRatio || up <= 0 { anchor = p; return step >= PetTuning.rubMinStepPoints ? .rub : .none }
        if up >= LiftTuning.liftUpPoints { lifted = true; return .lift }
        return step >= PetTuning.rubMinStepPoints ? .rub : .none
    }

    public mutating func up() -> Action {
        guard start != nil else { return .none }
        defer { start = nil; lifted = false }
        return lifted ? .drop : .click
    }

    /// 没等到 mouseUp 但键已经松了 (比如屏幕变动时窗口被重建): 拎着就放下, 否则当没发生过.
    public mutating func cancel() -> Action {
        let a = lifted ? Action.drop : .none
        start = nil; lifted = false
        return a
    }
}

/// 该画哪一帧: 第几阶段, 小摆还是大摆, 第几帧.
public struct LiftPose: Equatable {
    public var stage: Int
    public var big: Bool
    public var index: Int
    public init(stage: Int, big: Bool, index: Int) { self.stage = stage; self.big = big; self.index = index }
}

/// 拎着时的时间和速度. 大摆帧约定 (build_scruff.py 的 SWAY): 第 1-3 帧身子偏右, 第 5-7 帧偏左, 帧号越大越开.
public struct LiftMotion {
    private let cell: Double
    private var elapsed = 0.0
    private var lastX: Double
    private var lagLeft = 0.0
    private var lagBase = 0

    public init(x: Double, cell: Double) { lastX = x; self.cell = cell }

    public mutating func update(x: Double, dt: Double) {
        guard dt > 0 else { return }
        elapsed += dt
        let v = (x - lastX) / dt / cell
        lastX = x
        if abs(v) >= LiftTuning.dragCellsPerSec {
            lagBase = (v > 0 ? 5 : 1) + (abs(v) >= LiftTuning.fastCellsPerSec ? 1 : 0)   // 身子落在拖动方向的反面
            lagLeft = LiftTuning.lagHold
        } else {
            lagLeft = max(0, lagLeft - dt)
        }
    }

    public func pose(stages: Int) -> LiftPose {
        let beat = Int((elapsed + 1e-6) / LiftTuning.swayBeat)
        let drooped = elapsed < LiftTuning.droopAfter ? 0 : 1 + Int((elapsed - LiftTuning.droopAfter + 1e-6) / LiftTuning.droopStep)
        let stage = min(drooped, max(stages - 1, 0))
        return lagLeft > 0 ? LiftPose(stage: stage, big: true, index: lagBase + beat % 2)
                           : LiftPose(stage: stage, big: false, index: beat % 8)
    }
}

/// 拎起时猫图在屏幕上的矩形: 拎点这一格的中心对准鼠标, 帧再高也只向下长. dir<0 时图是镜像的.
public func liftedRect(mouse: CGPoint, grip: (col: Int, row: Int), cols: Int, rows: Int, cell: Double, dir: Double = 1) -> CGRect {
    let col = dir >= 0 ? grip.col : cols - 1 - grip.col
    let left = (Double(mouse.x) - (Double(col) + 0.5) * cell).rounded()
    let top = (Double(mouse.y) + (Double(grip.row) + 0.5) * cell).rounded()
    return CGRect(x: left, y: top - Double(rows) * cell, width: Double(cols) * cell, height: Double(rows) * cell)
}

/// 松手后 fall 姿势的脚底位置: 头留在原地不跳, 身子缩回去.
public func dropPoint(headMidX: Double, headTop: Double, cell: Double, cols: Int, rows: Int, footInset: Int, dir: Double) -> (x: Double, y: Double) {
    let w = Double(cols) * cell
    let head = headRect(frame: "fall", catRect: CGRect(x: 0, y: 0, width: w, height: Double(rows) * cell), cell: cell, dir: dir)
    return (headMidX - Double(head.midX) + w / 2, headTop - Double(head.maxY) + Double(footInset) * cell)
}

/// 拎起帧: 高度可以比皮肤高, 所以不放进 Skin. 文件和皮肤同目录, 缺失时猫退回现有姿势.
public struct ScruffSheet {
    public let palette: [Character: (r: UInt8, g: UInt8, b: UInt8)]
    public let cols: Int
    public let grip: (col: Int, row: Int)
    public let headMidCol: Int          // 头水平中心在第几列; 头顶在第 0 行
    private let stages: [(small: [[String]], big: [[String]])]

    public var stageCount: Int { stages.count }

    public static func path(besideSkin skin: String) -> String {
        ((skin as NSString).deletingLastPathComponent as NSString).appendingPathComponent("pet-scruff.json")
    }

    /// 文件不存在返回 nil; 存在但坏了才抛错.
    public static func load(path: String) throws -> ScruffSheet? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        return try parse(data)
    }

    public static func parse(_ data: Data) throws -> ScruffSheet {
        func bad(_ why: String) -> SkinError { SkinError(description: "拎起帧无效: \(why)") }
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let pal = obj["palette"] as? [String: String],
              let grip = obj["grip"] as? [Int], grip.count == 2,
              let headMidCol = obj["headMidCol"] as? Int,
              let raw = obj["stages"] as? [[String: Any]], !raw.isEmpty else { throw bad("缺字段") }
        var stages: [(small: [[String]], big: [[String]])] = []
        var cols = 0
        for st in raw {
            guard let small = st["small"] as? [[String]], let big = st["big"] as? [[String]],
                  small.count == 8, big.count == 8, let tall = small.first?.count, tall > 0 else { throw bad("每阶段要 8+8 帧") }
            if cols == 0 { cols = small[0].first?.count ?? 0 }
            for f in small + big where f.count != tall || f.contains(where: { $0.count != cols }) { throw bad("帧尺寸不一致") }
            stages.append((small, big))
        }
        guard cols > 0, (0..<cols).contains(grip[0]), (0..<stages[0].small[0].count).contains(grip[1]) else { throw bad("拎点越界") }
        return ScruffSheet(palette: try parsePalette(pal, source: "拎起帧"), cols: cols, grip: (grip[0], grip[1]),
                           headMidCol: headMidCol, stages: stages)
    }

    public func rows(_ p: LiftPose) -> [String] {
        let st = stages[min(max(p.stage, 0), stages.count - 1)]
        let fs = p.big ? st.big : st.small
        return fs[min(max(p.index, 0), fs.count - 1)]
    }

    public func rgba(_ rows: [String]) -> [UInt8] { rgbaPixels(rows, width: cols, palette: palette) }
}
