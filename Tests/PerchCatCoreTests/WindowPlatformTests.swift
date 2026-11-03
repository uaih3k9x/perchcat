import Testing
import CoreGraphics
@testable import PerchCatCore

// 用户真实布局 (AppKit 坐标). 只在测试里出现, 实现不写死.
private let builtin = ScreenInfo(frame: CGRect(x: 0, y: 0, width: 1512, height: 982),
                                 visible: CGRect(x: 0, y: 56, width: 1512, height: 888))
private let hp = ScreenInfo(frame: CGRect(x: -1048, y: 982, width: 2560, height: 1440),
                            visible: CGRect(x: -1048, y: 982, width: 2560, height: 1415))
private let e2219 = ScreenInfo(frame: CGRect(x: 1512, y: 769, width: 1080, height: 1920),
                               visible: CGRect(x: 1512, y: 769, width: 1080, height: 1895))
private let screens = [builtin, hp, e2219]
private let rects = screens.map(\.frame)
private let catH = 128.0

/// x, 顶边, 宽, 高; 默认 layer 0.
private func win(_ id: Int, x: Double, top: Double, w: Double, h: Double = 300, layer: Int = 0,
                 pid: Int32 = 1, alpha: Double = 1, bundle: String? = nil) -> WindowInfo {
    WindowInfo(rect: CGRect(x: x, y: top - h, width: w, height: h), layer: layer, alpha: alpha, pid: pid,
               id: id, bundleID: bundle)
}

private func build(_ ws: [WindowInfo], screens s: [ScreenInfo] = screens, minWidth: Double = 100) -> [Platform] {
    buildPlatforms(windows: ws, ownPID: 99, screens: s, catHeight: catH, minWidth: minWidth)
}
private func windowPlatforms(_ ps: [Platform]) -> [Platform] { ps.filter { $0.windowID != nil } }

struct PlatformBuildTests {
    @Test func cgConversionWorksOnExternalScreen() {
        // 真实数据: HP 上最大化的窗口, CG y=-1415 h=1415 -> AppKit 982..2397
        let r = cgToAppKit(CGRect(x: -1048, y: -1415, width: 2560, height: 1415), primaryHeight: 982)
        #expect(r == CGRect(x: -1048, y: 982, width: 2560, height: 1415))
    }

    @Test func everyScreenHasAFloor() {
        let floors = build([]).filter { $0.windowID == nil }
        #expect(floors == [
            Platform(lo: 0, hi: 1512, top: 56, base: 0),
            Platform(lo: -1048, hi: 1512, top: 982, base: 982),
            Platform(lo: 1512, hi: 2592, top: 769, base: 769),
        ])
    }

    @Test func filtersAbnormalWindows() {
        let ps = windowPlatforms(build([
            win(1, x: 100, top: 500, w: 400, layer: 25),     // 非普通层
            win(2, x: 100, top: 500, w: 400, pid: 99),       // 自己
            win(3, x: 100, top: 500, w: 80),                 // 太窄
            win(4, x: 100, top: 500, w: 400, h: 20),         // 太矮
            win(5, x: 100, top: 500, w: 400, alpha: 0),      // 透明
            win(6, x: 100, top: 500, w: 400, bundle: "com.apple.dock"),
            win(7, x: 100, top: 40, w: 400, h: 40),          // 顶边低于地板
        ]))
        #expect(ps.isEmpty)
    }

    @Test func nonParticipantsDoNotOcclude() {
        let back = win(2, x: 100, top: 500, w: 400)
        for front in [win(1, x: 200, top: 600, w: 200, layer: 25), win(1, x: 200, top: 600, w: 200, pid: 99),
                      win(1, x: 200, top: 600, w: 200, alpha: 0), win(1, x: 200, top: 600, w: 200, bundle: "com.apple.dock")] {
            #expect(windowPlatforms(build([front, back])) == [Platform(lo: 100, hi: 500, top: 500, windowID: 2)])
        }
    }

    @Test func menuBarSafeHeightIsRespectedOnEveryScreen() {
        let s = PlatformTuning.menuBarSafe
        // 用矮猫, 单测这条规则, 不受"头顶出屏"影响
        func one(_ w: WindowInfo) -> [Platform] {
            windowPlatforms(buildPlatforms(windows: [w], ownPID: 99, screens: screens, catHeight: 1, minWidth: 100))
        }
        #expect(one(win(1, x: 100, top: 982 - s + 1, w: 400)).isEmpty)
        #expect(one(win(1, x: 100, top: 982 - s, w: 400)).count == 1)
        #expect(one(win(1, x: 100, top: 2422 - s + 1, w: 400)).isEmpty)
        #expect(one(win(1, x: 1600, top: 2689 - s + 1, w: 400)).isEmpty)
        #expect(one(win(1, x: 1600, top: 2689 - s, w: 400)).count == 1)
    }

    @Test func catHeadMustFitBelowScreenTop() {
        #expect(windowPlatforms(build([win(1, x: 100, top: 982 - catH + 1, w: 400)])).isEmpty)
        #expect(windowPlatforms(build([win(1, x: 100, top: 982 - catH, w: 400)])).count == 1)
    }

    @Test func frontWindowCutsTheEdgeBehind() {
        let ps = windowPlatforms(build([
            win(1, x: 300, top: 700, w: 200),                // 前: 盖住 300..500
            win(2, x: 100, top: 500, w: 600),                // 后: 剩 100..300, 500..700
        ]))
        #expect(ps.contains(Platform(lo: 100, hi: 300, top: 500, windowID: 2)))
        #expect(ps.contains(Platform(lo: 500, hi: 700, top: 500, windowID: 2)))
        #expect(ps.contains(Platform(lo: 300, hi: 500, top: 700, windowID: 1)))
        #expect(ps.count == 3)
    }

    @Test func shortFragmentsAreDropped() {
        let ps = windowPlatforms(build([
            win(1, x: 150, top: 700, w: 400),                // 后面窗口左边只剩 100..150
            win(2, x: 100, top: 500, w: 600),
        ]))
        #expect(!ps.contains { $0.windowID == 2 && $0.lo == 100 })
        #expect(ps.contains(Platform(lo: 550, hi: 700, top: 500, windowID: 2)))
    }

    @Test func frontWindowBelowOrAboveTheEdgeDoesNotCut() {
        let above = win(1, x: 200, top: 800, w: 200, h: 100)   // 底边 700, 高于 500
        let below = win(3, x: 200, top: 400, w: 200, h: 100)   // 顶边 400, 低于 500
        let ps = windowPlatforms(build([above, below, win(2, x: 100, top: 500, w: 600)]))
        #expect(ps.contains(Platform(lo: 100, hi: 700, top: 500, windowID: 2)))
    }

    @Test func frontWindowTooSmallToStandOnStillOccludes() {
        let ps = windowPlatforms(build([
            win(1, x: 300, top: 600, w: 60, h: 200),         // 太窄不能站, 但会挡
            win(2, x: 100, top: 500, w: 600),
        ]))
        #expect(ps == [Platform(lo: 100, hi: 300, top: 500, windowID: 2), Platform(lo: 360, hi: 700, top: 500, windowID: 2)])
    }

    @Test func windowIsClippedToTheScreenThatShowsIt() {
        // 真实数据: 飞书 x=1333 宽 1512, 跨 HP/E2219, 大部分在 E2219 -> 只算 E2219 那段
        let ps = windowPlatforms(build([win(1, x: 1333, top: 1900, w: 1512, h: 887)]))
        #expect(ps == [Platform(lo: 1512, hi: 2592, top: 1900, windowID: 1)])
    }

    @Test func invisiblePartDoesNotOccludeOtherScreen() {
        // 窗口 1 大部分在 HP, 伸到 E2219 的部分看不见, 不该挡 E2219 上的窗口 2
        let ps = windowPlatforms(build([
            win(1, x: 1000, top: 1600, w: 600, h: 800),
            win(2, x: 1520, top: 1500, w: 500, h: 400),
        ]))
        #expect(ps.contains(Platform(lo: 1520, hi: 2020, top: 1500, windowID: 2)))
    }
}

struct CarryDeltaTests {
    @Test func followsMoveAndResizeRules() {
        let a = win(1, x: 100, top: 500, w: 400)
        #expect(carryDelta(windowID: 1, old: [a], new: [a]) == nil)
        let moved = carryDelta(windowID: 1, old: [a], new: [win(1, x: 130, top: 520, w: 400)])
        #expect(moved?.dx == 30 && moved?.dy == 20)
        // 改了宽度: 不算平移, 顶边照跟
        let resized = carryDelta(windowID: 1, old: [a], new: [win(1, x: 130, top: 520, w: 300)])
        #expect(resized?.dx == 0 && resized?.dy == 20)
        #expect(carryDelta(windowID: 1, old: [a], new: []) == nil)
    }
}

struct ArcScreenTests {
    private func arc(_ x0: Double, _ y0: Double, _ tx: Double, _ ty: Double) -> Arc {
        Arc(x0: x0, y0: y0, tx: tx, ty: ty, clearance: 24, g: 1360)
    }

    @Test func routesFollowRealAdjacency() {
        #expect(arcStaysOnScreens(arc(1450, 982, 1560, 769), screens: rects))     // HP -> E2219
        #expect(arcStaysOnScreens(arc(1560, 769, 1450, 982), screens: rects))     // E2219 -> HP
        #expect(arcStaysOnScreens(arc(700, 800, 700, 982), screens: rects))       // 笔记本窗口 -> HP 穿菜单栏
        #expect(arcStaysOnScreens(arc(700, 982, 700, 56), screens: rects))        // HP -> 笔记本
        #expect(arcStaysOnScreens(arc(1450, 760, 1560, 769), screens: rects))     // 笔记本高窗口 -> E2219
        #expect(!arcStaysOnScreens(arc(1450, 300, 1560, 769), screens: rects))    // 太低, 穿过屏幕外
        #expect(!arcStaysOnScreens(arc(-100, 982, 100, 56), screens: rects))      // HP 左半下面没屏
    }

    @Test func emptyScreensAllowEverything() {
        #expect(arcStaysOnScreens(arc(0, 0, 9999, 9999), screens: []))
    }
}

private let dt = 0.125
private func step(_ b: CatBrain, _ n: Int, _ ps: [Platform]) { for _ in 0..<n { b.tick(dt: dt, platforms: ps) } }

struct CatFollowTests {
    private let floorP = Platform(lo: 0, hi: 1512, top: 56, base: 0)

    @Test func carryMovesCatAndWalkTarget() {
        let w = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 0, rand: { 0.3 })   // r=0.3: 走
        step(b, 1, [floorP, w])
        let x0 = b.x
        b.carry(dx: 200, dy: 100)
        #expect(b.x == x0 + 200 && b.y == 600)
        let moved = Platform(lo: 300, hi: 1100, top: 600, windowID: 1)
        step(b, 200, [floorP, moved])
        #expect(!b.isFalling && b.y == 600)                      // 全程没走出挪动后的窗口
    }

    @Test func carryIgnoredInAir() {
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        step(b, 1, [floorP])
        #expect(b.isFalling)
        let x = b.x
        b.carry(dx: 200, dy: 100)
        #expect(b.x == x)
    }

    @Test func coveredEdgeDropsCatToNextPlatform() {
        let lower = Platform(lo: 0, hi: 900, top: 200, windowID: 2)
        let high = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        step(b, 2, [floorP, high, lower])
        // 前面来了个窗口, 平台碎成两段, 猫脚下这段没了
        let rest = Platform(lo: 500, hi: 900, top: 500, windowID: 1)
        step(b, 20, [floorP, rest, lower])
        #expect(b.y == 200)
    }

    @Test func risingFloorLiftsCat() {
        let b = CatBrain(scale: 4, x: 300, y: 56, sleepChance: 1, rand: { 0.5 })
        step(b, 2, [floorP])
        let higher = Platform(lo: 0, hi: 1512, top: 120, base: 0)
        step(b, 12, [higher])
        #expect(b.y == 120 && !b.isFalling)
    }

    @Test func droppingFloorLetsCatFall() {
        let b = CatBrain(scale: 4, x: 300, y: 120, sleepChance: 1, rand: { 0.5 })
        step(b, 2, [Platform(lo: 0, hi: 1512, top: 120, base: 0)])
        let lower = Platform(lo: 0, hi: 1512, top: 0, base: 0)
        step(b, 1, [lower])
        #expect(b.isFalling)
        step(b, 20, [lower])
        #expect(b.y == 0 && !b.isFalling)
    }

    @Test func risingFloorDoesNotGrabCatOnAnotherScreen() {
        // 猫站在笔记本地板; HP 地板 (Dock 换到 HP) 的 x 范围也盖着它, 但 y 不在 HP 里
        let hpFloor = Platform(lo: -1048, hi: 1512, top: 1035, base: 982)
        let b = CatBrain(scale: 4, x: 300, y: 56, sleepChance: 1, rand: { 0.5 })
        step(b, 3, [floorP, hpFloor])
        #expect(b.y == 56)
    }

    @Test func lostCatComesBackWhenItsScreenDisappears() {
        let hpFloor = Platform(lo: -1048, hi: 1512, top: 982, base: 982)
        let b = CatBrain(scale: 4, x: 2000, y: 769, sleepChance: 1, rand: { 0.5 })   // E2219 被拔
        step(b, 2, [floorP, hpFloor])
        #expect(b.x <= 1512 && !b.isFalling)
        #expect(b.y == 56 || b.y == 982)
    }
}

/// 确定性伪随机, 让仿真可复现.
private final class LCG {
    var s: UInt64
    init(_ seed: UInt64) { s = seed }
    func next() -> Double {
        s = s &* 6364136223846793005 &+ 1442695040888963407
        return Double(s >> 11) / Double(UInt64(1) << 53)
    }
}

struct RouteSimulationTests {
    /// 三块屏 + 笔记本上两级台阶 (地板够不到靠上的窗口) + E2219/HP 各一个窗口.
    private let ps = buildPlatforms(windows: [
        win(1, x: 500, top: 700, w: 900, h: 400),      // 笔记本靠上
        win(4, x: 100, top: 380, w: 350, h: 200),      // 笔记本台阶
        win(2, x: 1600, top: 1000, w: 500, h: 200),    // E2219
        win(3, x: 300, top: 1500, w: 800, h: 400),     // HP
    ], ownPID: 99, screens: screens, catHeight: 128, minWidth: 100)

    private func sim(sleepChance: Double, crossChance: Double, seed: UInt64, hours: Double,
                     visit: (Int) -> Void = { _ in }, crossed: (Int, Int) -> Void = { _, _ in },
                     jumped: () -> Void = {}) {
        let r = LCG(seed)
        let b = CatBrain(scale: 4, x: 756, y: 56, sleepChance: sleepChance, rand: { r.next() })
        b.screens = rects
        b.crossChance = crossChance
        var last = 0, wasArc = false
        for _ in 0..<Int(hours * 3600 / dt) {
            b.tick(dt: dt, platforms: ps)
            // 不变量: 任何时刻脚底都在某块屏里
            let i = screenIndex(b.x, b.y, rects)
            #expect(i != nil, "脚底跑到屏幕外: \(b.x), \(b.y)")
            guard let i else { return }
            visit(i)
            if !b.isAirborne, i != last { crossed(last, i); last = i }
            if b.isJumping, !wasArc { jumped() }
            wasArc = b.isJumping
        }
    }

    @Test func catVisitsAllScreensAndNeverLeavesThem() {
        var seen = Set<Int>(), pairs = Set<[Int]>()
        sim(sleepChance: 0, crossChance: 1, seed: 7, hours: 2,
            visit: { seen.insert($0) }, crossed: { pairs.insert([$0, $1]) })
        #expect(seen == [0, 1, 2])
        #expect(pairs.contains([1, 2]))      // HP -> E2219
        #expect(pairs.contains([2, 1]))      // E2219 -> HP
        #expect(pairs.contains([0, 1]))      // 笔记本 -> HP
        #expect(pairs.contains([1, 0]))      // HP -> 笔记本
    }

    @Test func defaultsKeepJumpsAndCrossingsRare() {
        var jumps = 0, crossings = 0
        sim(sleepChance: 0.55, crossChance: PlatformTuning.crossChance, seed: 11, hours: 8,
            crossed: { _, _ in crossings += 1 }, jumped: { jumps += 1 })
        let jph = Double(jumps) / 8, cph = Double(crossings) / 8
        print("仿真: 每小时跳 \(jph) 次, 跨屏 \(cph) 次")
        #expect(jph < 60)
        #expect(cph < 15)
        #expect(crossings > 0)
    }

    @Test func sameScreenOnlyWhenCrossingDisabled() {
        var seen = Set<Int>()
        sim(sleepChance: 0, crossChance: 0, seed: 3, hours: 2, visit: { seen.insert($0) })
        // 不会主动换屏. 空中掠过其他屏的 rect 不算, 这里只看没有跨屏落地.
        var pairs = 0
        sim(sleepChance: 0, crossChance: 0, seed: 3, hours: 2, crossed: { _, _ in pairs += 1 })
        #expect(pairs == 0)
    }
}

// 全屏时 Dock 藏了, visibleFrame 还留着 Dock 的高度: 地板要落到屏幕底边.
struct FullscreenFloorTests {
    private func floorTop(_ ps: [Platform], lo: Double) -> Double? {
        ps.first { $0.windowID == nil && $0.lo == lo }?.top
    }

    @Test func fullscreenWindowDropsFloorToScreenBottom() {
        let ps = build([win(1, x: 0, top: 982, w: 1512, h: 982)])
        #expect(floorTop(ps, lo: 0) == 0)
        #expect(floorTop(ps, lo: -1048) == 982)                  // 别的屏不受影响
    }

    @Test func notchFullscreenStopsBelowMenuBarButStillCounts() {
        let ps = build([win(1, x: 0, top: 944, w: 1512, h: 944)])
        #expect(floorTop(ps, lo: 0) == 0)
        #expect(windowPlatforms(ps).isEmpty)                     // 也不能站在刘海下面的窗口顶上
    }

    @Test func maximizedWindowAboveDockKeepsFloor() {
        #expect(floorTop(build([win(1, x: 0, top: 944, w: 1512, h: 888)]), lo: 0) == 56)
    }

    @Test func narrowOrIgnoredWindowsKeepFloor() {
        #expect(floorTop(build([win(1, x: 0, top: 982, w: 800, h: 982)]), lo: 0) == 56)
        #expect(floorTop(build([win(1, x: 0, top: 982, w: 1512, h: 982, pid: 99)]), lo: 0) == 56)
        #expect(floorTop(build([win(1, x: 0, top: 982, w: 1512, h: 982, bundle: "com.apple.dock")]), lo: 0) == 56)
    }
}

// 地板变化和睡着的猫.
struct FloorJumpAndSleepDropTests {
    private let low = Platform(lo: 0, hi: 1512, top: 0, base: 0)
    private let high = Platform(lo: 0, hi: 1512, top: 56, base: 0)

    private func sleeper(on ps: [Platform], x: Double = 300, y: Double) -> CatBrain {
        let b = CatBrain(scale: 4, x: x, y: y, sleepChance: 1, rand: { 0.5 })
        for _ in 0..<12 { b.tick(dt: 0.125, platforms: ps) }
        return b
    }

    @Test func risingFloorMakesCatJumpInsteadOfTeleport() {
        let b = CatBrain(scale: 4, x: 300, y: 0, sleepChance: 0, rand: { 0.5 })
        b.tick(dt: 0.125, platforms: [low])
        b.tick(dt: 0.125, platforms: [high])
        #expect(b.isJumping && b.y < 56)
        var n = 0
        while b.isJumping && n < 40 { b.tick(dt: 0.125, platforms: [high]); n += 1 }
        #expect(!b.isJumping && b.y == 56 && !b.isFalling)
    }

    @Test func tinyFloorChangeJustSnaps() {
        let b = CatBrain(scale: 4, x: 300, y: 54, sleepChance: 0, rand: { 0.5 })
        b.tick(dt: 0.125, platforms: [high])
        #expect(!b.isJumping && b.y == 56)
    }

    @Test func sleepingCatIsNotCarriedByItsWindow() {
        let w = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let b = sleeper(on: [high, w], y: 500)
        #expect(b.isSleeping)
        b.carry(dx: 200, dy: 100)
        #expect(b.x == 300 && b.y == 500)
    }

    @Test func awakeCatStillCarried() {
        let w = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 0, rand: { 0.3 })
        b.tick(dt: 0.125, platforms: [high, w])
        b.carry(dx: 200, dy: 0)
        #expect(b.x > 300)
    }

    @Test func droppedSleeperShakesThenSleepsAgain() {
        let w = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let b = sleeper(on: [high, w], y: 500)
        #expect(b.isSleeping)
        var seen: [String] = []
        for _ in 0..<80 {
            b.tick(dt: 0.125, platforms: [high])                 // 窗口被拖走
            if seen.last != b.currentAnim { seen.append(b.currentAnim) }
        }
        #expect(seen.first == "fall")
        #expect(seen.contains("landing") && seen.contains("shake"))
        #expect(b.y == 56 && b.isSleeping)
    }

    @Test func thrownCatDoesNotGoBackToSleep() {
        let b = CatBrain(scale: 4, x: 300, y: 56, sleepChance: 0, rand: { 0.5 })
        b.tick(dt: 0.125, platforms: [high])
        b.beginLift(x: 300, y: 400)
        b.release(x: 300, y: 400)
        var seen: Set<String> = []
        for _ in 0..<60 { b.tick(dt: 0.125, platforms: [high]); seen.insert(b.currentAnim) }
        #expect(!seen.contains("shake") && !b.isSleeping)
    }

    @Test func shakeFramesExistInEveryBeat() {
        let b = sleeper(on: [high, Platform(lo: 100, hi: 900, top: 500, windowID: 1)], y: 500)
        var frames: Set<String> = []
        for _ in 0..<60 {
            b.tick(dt: 0.125, platforms: [high])
            if b.currentAnim == "shake" { frames.insert(b.frame()) }
        }
        #expect(frames.count >= 2)                               // 真的在抖, 不是一张静图
    }
}

// 抖毛: 两帧来回. 有 pet-shake.json 用它 (带透明拖影, 画布比猫大), 没有就退回眯眼坐着.
struct ShakeSheetTests {
    @Test func parsesAndPremultipliesAlpha() throws {
        let s = try ShakeSheet.parse(shakeJSON(pad: 1, width: 2, height: 1, frames: ["ff00007f" + "000000ff", "00ff00ff" + "00000000"]))
        #expect(s.pad == 1 && s.width == 2 && s.height == 1 && s.frames.count == 2)
        #expect(s.frames[0][0] == 127 && s.frames[0][3] == 127)    // 红 * alpha
        #expect(s.frames[1][1] == 255)
    }

    @Test func rejectsWrongSize() {
        #expect(throws: (any Error).self) { try ShakeSheet.parse(shakeJSON(pad: 1, width: 2, height: 1, frames: ["ff0000ff"])) }
        #expect(throws: (any Error).self) { try ShakeSheet.parse(notJSON()) }
    }

    @Test func frameIndexFromName() {
        #expect(ShakeSheet.index(of: "shake1") == 1)
        #expect(ShakeSheet.index(of: "shake0") == 0)
        #expect(ShakeSheet.index(of: "sit") == nil)
    }

    @Test func skinFallsBackToHappySit() throws {
        let skin = try Skin.parse(json: """
        {"palette":{"K":"#000000"},"poses":{"stand":["K."],"sit":["K."],"sit@happy":[".K"]}}
        """)
        #expect(skin.resolve("shake1") == [".K"])
        #expect(skin.resolve("nope") == ["K."])
    }

    @Test func catAlternatesTwoFramesAndCannotBePetted() {
        let w = Platform(lo: 100, hi: 900, top: 500, windowID: 1)
        let floorP = Platform(lo: 0, hi: 1512, top: 56, base: 0)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        for _ in 0..<12 { b.tick(dt: 0.125, platforms: [floorP, w]) }
        var n = 0
        while b.currentAnim != "shake" && n < 80 { b.tick(dt: 0.125, platforms: [floorP]); n += 1 }
        #expect(b.currentAnim == "shake")
        b.stroke()
        #expect(!b.isPetted)
        var seen: Set<String> = []
        while b.currentAnim == "shake" && n < 200 { seen.insert(b.frame()); b.tick(dt: 0.125, platforms: [floorP]); n += 1 }
        #expect(seen == ["shake0", "shake1"])
    }
}
