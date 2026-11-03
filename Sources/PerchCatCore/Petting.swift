import CoreGraphics

/// 摸猫的阈值. 时间单位秒, 距离单位"皮肤格" (1 格 = scale 点).
public enum PetTuning {
    // 判定"在蹭": 头宽 14 格, 一次直线穿过去攒不够
    public static let rubOnCells = 40.0         // 攒够这么多距离才算开始摸
    public static let rubCapCells = 60.0        // 攒的上限
    public static let rubLeakCellsPerSec = 10.0 // 每秒漏掉这么多, 比这慢的挪动永远攒不起来
    public static let rubMaxStepCells = 14.0    // 单次采样最多记这么多, 防一下跳很远
    public static let rubMinStepPoints = 1.0    // 比这小的抖动不算动

    // 摸的进程 (从开始摸算起)
    public static let happyAfter = 3.0          // squint -> happy
    public static let kneadAfter = 8.0          // 开始踩奶
    public static let fedUpAfter = 30.0         // 连续摸这么久就烦了
    public static let release = 1.5             // 停手这么久算摸完, 烦躁计时也清零
    public static let stirHalf = 1.0            // 睡着被摸: 半睁眼多久后眯回去

    // 摸烦了
    public static let puffSeconds = 1.6
    public static let walkAwayCells = 40.0      // 走开多远
    public static let cooldown = 60.0           // 从炸毛算起, 这期间摸它不理
}

/// 把鼠标采样变成"这一下算不算在蹭": 头上累计移动距离, 随时间漏掉. 悬停不攒.
public struct RubSensor {
    private let cell: Double
    private var last: CGPoint?
    private var charge = 0.0

    public init(cell: Double) { self.cell = cell }

    public mutating func sample(_ p: CGPoint, inHead: Bool, dt: Double) -> Bool {
        let moved = last.map { hypot(p.x - $0.x, p.y - $0.y) } ?? 0
        last = p
        charge = max(0, charge - PetTuning.rubLeakCellsPerSec * cell * dt)
        guard inHead, moved >= PetTuning.rubMinStepPoints else { return false }
        charge = min(charge + min(moved, PetTuning.rubMaxStepCells * cell), PetTuning.rubCapCells * cell)
        return charge >= PetTuning.rubOnCells * cell
    }
}

private let headCols = 14, headRows = 13, standHead = (col: 10, row: 5)

/// 各姿势头的左上角在第几列第几行 (对着皮肤数出来的). 没列的 (走路等) 和 stand 差不到一格.
private let headAt: [String: (col: Int, row: Int)] = [
    "sit": (7, 3), "lick0": (7, 3), "lick1": (7, 3), "lick2": (7, 4),
    "knead0": (10, 7), "knead1": (10, 8), "sleep0": (8, 9), "sleep1": (8, 10),
    "half": (10, 7), "crouch": (10, 9), "fall": (10, 7),
]

/// 猫头在屏幕上的矩形. frame 如 "knead1@happy"; catRect 是整张图的矩形; dir<0 时图是镜像的.
public func headRect(frame: String, catRect: CGRect, cell: Double, dir: Double) -> CGRect {
    let pose = frame.split(separator: "@").first.map(String.init) ?? frame
    let h = headAt[pose] ?? standHead
    let w = Double(headCols) * cell, tall = Double(headRows) * cell
    let left = dir >= 0 ? Double(h.col) * cell : Double(catRect.width) - Double(h.col) * cell - w
    return CGRect(x: Double(catRect.minX) + left, y: Double(catRect.maxY) - Double(h.row) * cell - tall, width: w, height: tall)
}
