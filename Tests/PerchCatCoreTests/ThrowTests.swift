import Testing
import CoreGraphics
@testable import PerchCatCore

private let floorP = Platform(lo: 0, hi: 1440, top: 70)
private let cell = 4.0

private func thrown(x: Double = 300, y: Double = 600, vx: Double, vy: Double = 0) -> CatBrain {
    let b = CatBrain(scale: cell, x: 300, y: 70, sleepChance: 0, rand: { 0.5 })
    b.beginLift(x: x, y: y)
    b.release(x: x, y: y, vx: vx, vy: vy)
    return b
}

private func flyUntilLanded(_ b: CatBrain, _ ps: [Platform], maxSteps: Int = 400) {
    for _ in 0..<maxSteps where b.isFalling { b.tick(dt: 0.05, platforms: ps) }
}

struct ThrowTrackerTests {
    @Test func flickGivesTheSpeedOfTheLastMoment() {
        var t = ThrowTracker()
        t.add(x: 0, y: 0, at: 0)
        t.add(x: 0, y: 0, at: 1)            // 拎着停了很久, 不算
        t.add(x: 50, y: 0, at: 1.05)
        t.add(x: 100, y: 10, at: 1.1)
        let v = t.velocity(at: 1.1)
        #expect(abs(v.x - 1000) < 1e-6)
        #expect(abs(v.y - 100) < 1e-6)
    }

    @Test func pausingBeforeLettingGoDropsStraight() {
        var t = ThrowTracker()
        t.add(x: 0, y: 0, at: 1)
        t.add(x: 100, y: 0, at: 1.05)
        let v = t.velocity(at: 1.5)
        #expect(v.x == 0 && v.y == 0)
    }

    @Test func singleSampleHasNoSpeed() {
        var t = ThrowTracker()
        t.add(x: 5, y: 5, at: 1)
        #expect(t.velocity(at: 1).x == 0)
    }
}

struct ThrowVelocityTests {
    @Test func slowLetGoIsAPlainDrop() {
        let v = throwVelocity(mouseVX: 100, mouseVY: -100, cell: cell)
        #expect(v.vx == 0 && v.vy == 0)
    }

    @Test func hardFlickIsCapped() {
        let v = throwVelocity(mouseVX: 90000, mouseVY: 90000, cell: cell)
        #expect(v.vx == ThrowTuning.maxSideCells * cell)
        #expect(v.vy == ThrowTuning.maxUpCells * cell)
        #expect(throwVelocity(mouseVX: -90000, mouseVY: 0, cell: cell).vx == -ThrowTuning.maxSideCells * cell)
    }

    @Test func downwardFlickIsNotCappedByTheUpLimit() {
        #expect(throwVelocity(mouseVX: 0, mouseVY: -3000, cell: cell).vy < -ThrowTuning.maxUpCells * cell)
    }
}

struct ThrownCatTests {
    @Test func throwRightLandsToTheRight() {
        let b = thrown(vx: 600)
        flyUntilLanded(b, [floorP])
        #expect(!b.isFalling)
        #expect(b.y == 70)
        #expect(b.x > 500)
    }

    @Test func plainReleaseStillFallsStraight() {
        let b = thrown(vx: 0)
        flyUntilLanded(b, [floorP])
        #expect(b.x == 300)
    }

    @Test func upwardThrowRisesFirst() {
        let b = thrown(vx: 0, vy: 400)
        b.tick(dt: 0.05, platforms: [floorP])
        #expect(b.y > 600)
        flyUntilLanded(b, [floorP])
        #expect(b.y == 70)
    }

    @Test func screenEdgeBouncesInsteadOfTeleporting() {
        let b = thrown(x: 1300, vx: 1600)
        var maxX = 0.0
        for _ in 0..<400 where b.isFalling {
            b.tick(dt: 0.05, platforms: [floorP])
            maxX = max(maxX, b.x)
        }
        #expect(maxX + b.halfWidth <= 1440 + 1e-6)
        #expect(b.x < 1440 - b.halfWidth)       // 弹回来了
        #expect(b.y == 70)
    }

    @Test func landsOnAWindowItFliesOver() {
        let win = Platform(lo: 700, hi: 1000, top: 300, windowID: 7)
        let b = thrown(x: 300, y: 500, vx: 900)
        flyUntilLanded(b, [floorP, win])
        #expect(b.y == 300)
        #expect(b.x >= 700 && b.x <= 1000)
    }

    @Test func fastThrowCannotTunnelPastAWindowEdgeAtTheCrossing() {
        // 一步里从窗口左边外面飞到右边外面, 但穿过窗口高度时正好在窗口上方
        let win = Platform(lo: 400, hi: 500, top: 100)
        let r = fallStep(x: 300, y: 120, vy: -400, dt: 0.1, g: 0, platforms: [floorP, win], vx: 2200, halfWidth: 0)
        #expect(r.landed?.top == 100)
        #expect(r.x > 400 && r.x < 500)
    }

    @Test func ceilingStopsTheRise() {
        let screens = [CGRect(x: 0, y: 0, width: 1440, height: 900)]
        let r = fallStep(x: 300, y: 895, vy: 500, dt: 0.1, g: 0, platforms: [floorP], vx: 0, halfWidth: 0, screens: screens)
        #expect(r.y == 895)
        #expect(r.vy == 0)
    }

    @Test func gapBetweenScreensActsAsAWall() {
        // 笔记本右边的竖屏底部更高: 低处甩过去撞边界, 高处能飞过去
        let laptop = Platform(lo: 0, hi: 1512, top: 56)
        let tall = Platform(lo: 1512, hi: 2592, top: 769)
        let low = fallStep(x: 1450, y: 500, vy: 0, dt: 0.1, g: 0, platforms: [laptop, tall], vx: 1000, halfWidth: 48)
        #expect(low.x == 1450)
        #expect(low.vx < 0)
        let high = fallStep(x: 1450, y: 800, vy: 0, dt: 0.1, g: 0, platforms: [laptop, tall], vx: 1000, halfWidth: 48)
        #expect(high.x == 1550)
        #expect(high.vx == 1000)
    }
}
