import Testing
import CoreGraphics
@testable import PerchCatCore

private let floorP = Platform(lo: 0, hi: 1440, top: 70)
private let cell = 4.0
private let dt = 0.125

private func cat(sleepChance: Double = 0) -> CatBrain {
    CatBrain(scale: cell, x: 300, y: 70, sleepChance: sleepChance, rand: { 0.5 })
}

private func frames(_ tall: Int, _ n: Int = 8) -> [[String]] {
    Array(repeating: Array(repeating: String(repeating: "K", count: 24), count: tall), count: n)
}

struct PressGestureTests {
    @Test func clickWithoutMovingPokes() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.isPressed)
        #expect(g.up() == .click)
        #expect(!g.isPressed)
    }

    @Test func jitterBelowThresholdIsStillAClick() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.moved(CGPoint(x: 10.5, y: 10.2)) == .none)
        #expect(g.up() == .click)
    }

    @Test func pullingUpLiftsThenMovesThenDrops() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.moved(CGPoint(x: 10, y: 15)) == .rub)      // 还没够高
        #expect(g.moved(CGPoint(x: 11, y: 30)) == .lift)
        #expect(g.moved(CGPoint(x: 30, y: 10)) == .move)     // 拎起后随便往哪拖
        #expect(g.moved(CGPoint(x: 10, y: -90)) == .move)
        #expect(g.up() == .drop)
        #expect(g.up() == .none)
    }

    @Test func sidewaysPressIsARubNotALift() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.moved(CGPoint(x: 60, y: 10)) == .rub)
        #expect(g.moved(CGPoint(x: 10, y: 11)) == .rub)
        #expect(g.moved(CGPoint(x: 80, y: 13)) == .rub)
        #expect(g.up() == .click)
    }

    @Test func pushingDownNeverLifts() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.moved(CGPoint(x: 10, y: -100)) == .rub)
        #expect(g.up() == .click)
    }

    @Test func shallowDiagonalIsARub() {
        var g = PressGesture()
        g.down(.zero)
        #expect(g.moved(CGPoint(x: 100, y: 30)) == .rub)
    }

    @Test func canPullUpAfterRubbing() {
        var g = PressGesture()
        g.down(CGPoint(x: 10, y: 10))
        #expect(g.moved(CGPoint(x: 90, y: 10)) == .rub)
        #expect(g.moved(CGPoint(x: 90, y: -40)) == .rub)
        #expect(g.moved(CGPoint(x: 92, y: -15)) == .lift)    // 从最后的低点往上提
    }

    @Test func movingWithoutPressDoesNothing() {
        var g = PressGesture()
        #expect(g.moved(CGPoint(x: 100, y: 100)) == .none)
    }

    @Test func cancelOnlyDropsWhenLifted() {
        var a = PressGesture()
        a.down(.zero)
        #expect(a.cancel() == .none)                         // 丢了 mouseUp: 没拖动就不算点击
        #expect(!a.isPressed)
        var b = PressGesture()
        b.down(.zero)
        _ = b.moved(CGPoint(x: 0, y: 50))
        #expect(b.cancel() == .drop)
        var c = PressGesture()
        c.down(.zero)
        _ = c.moved(CGPoint(x: 50, y: 0))
        #expect(c.cancel() == .none)                         // 只是在蹭
    }
}

struct LiftMotionTests {
    @Test func staysOnFirstStageThenDroopsStepByStepToLast() {
        var m = LiftMotion(x: 0, cell: cell)
        var seen: [Int] = []
        for _ in 0..<80 {
            m.update(x: 0, dt: dt)
            seen.append(m.pose(stages: 5).stage)
        }
        #expect(seen.first == 0)
        #expect(seen.last == 4)
        #expect(seen == seen.sorted())                       // 只往下坠
        #expect(Set(seen) == [0, 1, 2, 3, 4])                // 不跳帧
        #expect(seen.filter { $0 == 0 }.count * Int(dt * 1000) >= Int(LiftTuning.droopAfter * 1000) - 125)
    }

    @Test func singleStageSheetStaysOnIt() {
        var m = LiftMotion(x: 0, cell: cell)
        for _ in 0..<80 { m.update(x: 0, dt: dt) }
        #expect(m.pose(stages: 1).stage == 0)
    }

    @Test func hangingUsesSmallSwayCycle() {
        var m = LiftMotion(x: 0, cell: cell)
        var idx: [Int] = []
        for _ in 0..<8 {
            m.update(x: 0, dt: dt)
            let p = m.pose(stages: 5)
            #expect(!p.big)
            idx.append(p.index)
        }
        #expect(Set(idx).count == 8)
    }

    /// 大摆第 1-3 帧身子偏右, 第 5-7 帧偏左 (见 build_scruff.py 的 SWAY).
    func bigPose(speedCells: Double) -> LiftPose {
        var m = LiftMotion(x: 0, cell: cell)
        var x = 0.0
        x += speedCells * cell * dt
        m.update(x: x, dt: dt)
        return m.pose(stages: 5)
    }

    @Test func draggingRightSwingsBodyLeft() {
        let p = bigPose(speedCells: 200)
        #expect(p.big)
        #expect((5...7).contains(p.index))
    }

    @Test func draggingLeftSwingsBodyRight() {
        let p = bigPose(speedCells: -200)
        #expect(p.big)
        #expect((1...3).contains(p.index))
    }

    @Test func slowMoveKeepsSmallSway() {
        #expect(!bigPose(speedCells: 2).big)
    }

    @Test func fasterDragSwingsFarther() {
        // 同一拍比较: 快的帧号离中心更远
        #expect(bigPose(speedCells: 200).index > bigPose(speedCells: LiftTuning.dragCellsPerSec + 1).index)
    }

    @Test func stoppingReturnsToSmallSway() {
        var m = LiftMotion(x: 0, cell: cell)
        m.update(x: 200, dt: dt)
        #expect(m.pose(stages: 5).big)
        for _ in 0..<8 { m.update(x: 200, dt: dt) }
        #expect(!m.pose(stages: 5).big)
    }

    @Test func zeroDtDoesNotBlowUp() {
        var m = LiftMotion(x: 0, cell: cell)
        m.update(x: 100, dt: 0)
        #expect(!m.pose(stages: 5).big)
    }
}

struct LiftGeometryTests {
    @Test func gripCellSitsUnderMouse() {
        let r = liftedRect(mouse: CGPoint(x: 500, y: 800), grip: (11, 3), cols: 24, rows: 34, cell: cell)
        #expect(r.width == 96)
        #expect(r.height == 136)
        // 第 11 列第 3 行的格子
        let gx = r.minX + 11 * cell, gy = r.maxY - 4 * cell
        #expect(CGRect(x: gx, y: gy, width: cell, height: cell).contains(CGPoint(x: 500, y: 800)))
    }

    @Test func tallFrameGrowsDownwardOnly() {
        let a = liftedRect(mouse: CGPoint(x: 500, y: 800), grip: (11, 3), cols: 24, rows: 24, cell: cell)
        let d = liftedRect(mouse: CGPoint(x: 500, y: 800), grip: (11, 3), cols: 24, rows: 34, cell: cell)
        #expect(a.maxY == d.maxY)
        #expect(a.minX == d.minX)
        #expect(d.minY == a.minY - 40)
    }

    @Test func mirroredFrameMirrorsGrip() {
        let r = liftedRect(mouse: CGPoint(x: 500, y: 800), grip: (17, 9), cols: 24, rows: 24, cell: cell, dir: -1)
        // 镜像后那一格在第 6 列
        #expect(CGRect(x: r.minX + 6 * cell, y: r.maxY - 10 * cell, width: cell, height: cell).contains(CGPoint(x: 500, y: 800)))
    }

    @Test func staysOnWholePoints() {
        let r = liftedRect(mouse: CGPoint(x: 500.3, y: 800.7), grip: (11, 3), cols: 24, rows: 24, cell: cell)
        #expect(r.minX == r.minX.rounded())
        #expect(r.minY == r.minY.rounded())
    }

    @Test func dropKeepsHeadWhereItWas() {
        for dir in [1.0, -1.0] {
            let p = dropPoint(headMidX: 500, headTop: 800, cell: cell, cols: 24, rows: 24, footInset: 1, dir: dir)
            // 猫的图矩形, 算法同 main.swift 的 catRect
            let rect = CGRect(x: p.x - 48, y: p.y - cell, width: 96, height: 96)
            let head = headRect(frame: "fall", catRect: rect, cell: cell, dir: dir)
            #expect(head.midX == 500)
            #expect(head.maxY == 800)
        }
    }
}

struct LiftBrainTests {
    @Test func liftStopsPlanningAndFollowsMouse() {
        let b = cat()
        b.tick(dt: dt, platforms: [floorP])
        b.beginLift(x: 600, y: 700)
        #expect(b.isLifted)
        #expect(b.isAirborne)
        for _ in 0..<40 { b.tick(dt: dt, platforms: [floorP]) }
        #expect(b.x == 600)
        #expect(b.y == 700)                                  // 不下落
        #expect(!b.isFalling)
        b.liftTo(x: 650, y: 720)
        #expect(b.x == 650)
    }

    @Test func releaseFallsToFloorAndResumes() {
        let b = cat()
        b.beginLift(x: 600, y: 700)
        b.tick(dt: dt, platforms: [floorP])
        b.release(x: 590, y: 500)
        #expect(!b.isLifted)
        #expect(b.isFalling)
        #expect(b.frame() == "fall")
        var seen: [String] = []
        for _ in 0..<40 {
            b.tick(dt: dt, platforms: [floorP])
            seen.append(b.currentAnim)
        }
        #expect(!b.isFalling)
        #expect(b.y == 70)
        #expect(seen.contains("landing"))
    }

    @Test func releaseLandsOnWindowBelowNotFloor() {
        let win = Platform(lo: 400, hi: 900, top: 300, windowID: 1)
        let b = cat()
        b.beginLift(x: 600, y: 700)
        b.release(x: 600, y: 600)
        for _ in 0..<40 { b.tick(dt: dt, platforms: [floorP, win]) }
        #expect(b.y == 300)
    }

    @Test func releaseOnOtherScreenLandsOnThatFloor() {
        let other = Platform(lo: 1440, hi: 3000, top: 900, base: 900)
        let b = cat()
        b.beginLift(x: 2000, y: 1200)
        b.release(x: 2000, y: 1100)
        for _ in 0..<40 { b.tick(dt: dt, platforms: [floorP, other]) }
        #expect(b.y == 900)
    }

    @Test func releaseOffEveryScreenIsRescued() {
        let b = cat()
        b.beginLift(x: 5000, y: 100)
        b.release(x: 5000, y: 40)
        for _ in 0..<5 { b.tick(dt: dt, platforms: [floorP]) }
        #expect(b.x <= 1440)
        #expect(b.y == 70)
    }

    @Test func pokeAndStrokeIgnoredWhileLifted() {
        let b = cat()
        b.beginLift(x: 600, y: 700)
        b.poke()
        b.stroke()
        #expect(!b.isPetted)
        #expect(b.bubble == nil)
    }

    @Test func liftInterruptsPettingAndClearsLook() {
        let b = cat()
        b.tick(dt: dt, platforms: [floorP])
        b.stroke()
        #expect(b.isPetted)
        b.beginLift(x: 600, y: 700)
        #expect(!b.isPetted)
        #expect(b.frame() == "fall")
    }

    @Test func liftInterruptsJumpAndFall() {
        let hi = Platform(lo: 100, hi: 600, top: 500, windowID: 1)
        let b = CatBrain(scale: cell, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        b.tick(dt: dt, platforms: [floorP, hi])
        b.tick(dt: dt, platforms: [floorP])
        #expect(b.isFalling)
        b.beginLift(x: 300, y: 450)
        #expect(!b.isFalling)
        for _ in 0..<5 { b.tick(dt: dt, platforms: [floorP]) }
        #expect(b.y == 450)
    }

    @Test func liftWakesSleepingCat() {
        let b = cat(sleepChance: 1)
        for _ in 0..<12 { b.tick(dt: dt, platforms: [floorP]) }
        #expect(b.isSleeping)
        b.beginLift(x: 300, y: 300)
        #expect(!b.isSleeping)
    }

    @Test func petCooldownSurvivesBeingLifted() {
        let b = cat()
        b.tick(dt: dt, platforms: [floorP])
        for _ in 0..<Int(PetTuning.fedUpAfter / dt + 2) { b.stroke(); b.tick(dt: dt, platforms: [floorP]) }
        b.beginLift(x: 300, y: 300)
        b.release(x: 300, y: 200)
        for _ in 0..<40 { b.tick(dt: dt, platforms: [floorP]) }
        b.stroke()
        #expect(!b.isPetted)
    }

    @Test func carryIgnoredWhileLifted() {
        let b = cat()
        b.beginLift(x: 600, y: 700)
        b.carry(dx: 50, dy: 50)
        #expect(b.x == 600)
    }

    @Test func liftPoseFollowsMotion() {
        let b = cat()
        #expect(b.liftPose(stages: 5) == nil)
        b.beginLift(x: 0, y: 700)
        b.tick(dt: dt, platforms: [floorP])
        #expect(b.liftPose(stages: 5)?.stage == 0)
        b.liftTo(x: 400, y: 700)
        b.tick(dt: dt, platforms: [floorP])
        let p = b.liftPose(stages: 5)
        #expect(p?.big == true)
        #expect((5...7).contains(p?.index ?? 0))
    }
}

struct ScruffSheetTests {
    @Test func missingFileIsNilNotError() throws {
        #expect(try ScruffSheet.load(path: "/nonexistent/pet-scruff.json") == nil)
    }

    @Test func pathIsBesideTheSkin() {
        #expect(ScruffSheet.path(besideSkin: "/a/b/pet-skin.json") == "/a/b/pet-scruff.json")
    }

    @Test func parsesVariableHeightStages() throws {
        let s = try ScruffSheet.parse(sheetJSON(stages: [
            ["name": "A", "small": frames(24), "big": frames(24)],
            ["name": "D", "small": frames(34), "big": frames(34)],
        ]))
        #expect(s.cols == 24)
        #expect(s.grip.col == 11)
        #expect(s.grip.row == 3)
        #expect(s.headMidCol == 12)
        #expect(s.stageCount == 2)
        let rows = s.rows(LiftPose(stage: 1, big: true, index: 7))
        #expect(rows.count == 34)
        #expect(s.rgba(rows).count == 24 * 34 * 4)
        #expect(Array(s.rgba(rows)[0..<4]) == [0, 0, 0, 255])
    }

    @Test func rejectsBrokenSheets() {
        for bad in [
            notJSON(),
            sheetJSON(stages: []),
            sheetJSON(stages: [["name": "A", "small": frames(24, 7), "big": frames(24)]]),       // 帧数不对
            sheetJSON(stages: [["name": "A", "small": frames(24), "big": frames(25)]]),          // 同阶段高度不一
            sheetJSON(stages: [["name": "A", "small": [[String(repeating: "K", count: 23)]] + frames(24, 7), "big": frames(24)]]),
            sheetJSON(stages: [["name": "A", "small": frames(24), "big": frames(24)]], grip: [99, 3]),
        ] {
            #expect(throws: (any Error).self) { _ = try ScruffSheet.parse(bad) }
        }
    }

    /// 用户的真皮肤不进仓库, 文件不在就跳过.
    @Test func realSheetIfPresent() throws {
        let path = ScruffSheet.path(besideSkin: realSkinPath())
        guard let s = try ScruffSheet.load(path: path) else { return }
        let hs = (0..<s.stageCount).map { s.rows(LiftPose(stage: $0, big: false, index: 0)).count }
        #expect(hs.first == 24)
        #expect(hs.last == 34)
        #expect(hs == hs.sorted())
        #expect(hs.count >= 4)                               // A + 两三帧过渡 + D
    }
}
