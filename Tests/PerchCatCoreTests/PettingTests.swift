import Testing
import CoreGraphics
@testable import PerchCatCore

private let floorP = Platform(lo: 0, hi: 1440, top: 70)
private let dt = 0.125

/// 跑 seconds 秒. stroking 时每一拍都蹭一下. 返回沿途见过的帧.
@discardableResult
private func run(_ b: CatBrain, _ seconds: Double, stroking: Bool = false, on ps: [Platform] = [floorP]) -> [String] {
    var seen: [String] = []
    for _ in 0..<Int((seconds / dt).rounded()) {
        if stroking { b.stroke() }
        b.tick(dt: dt, platforms: ps)
        seen.append(b.frame())
    }
    return seen
}

private func awakeCat() -> CatBrain {
    let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.5 })  // 不睡, 在地板上来回走
    run(b, 0.25)
    return b
}

private func sleepingCat() -> CatBrain {
    let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 1, rand: { 0.5 })  // 站 0.5 坐 0.5 然后睡
    run(b, 1.5)
    #expect(b.isSleeping)
    return b
}

struct RubSensorTests {
    let cell = 4.0

    /// 在 x0 起点, 以 stepX 每拍来回 (每 period 拍换一次方向), 数蹭到的拍数.
    func rubCount(_ s: inout RubSensor, samples: Int, step: Double, period: Int, inHead: Bool = true) -> Int {
        var x = 100.0, n = 0, d = 1.0
        for i in 0..<samples {
            if i % period == 0 { d = -d }
            x += d * step
            if s.sample(CGPoint(x: x, y: 50), inHead: inHead, dt: 0.1) { n += 1 }
        }
        return n
    }

    @Test func hoverIsNotPetting() {
        var s = RubSensor(cell: cell)
        for _ in 0..<100 {
            let rub = s.sample(CGPoint(x: 10, y: 10), inHead: true, dt: 0.1)
            #expect(!rub)
        }
    }

    @Test func rubbingBackAndForthTriggersWithinASecond() {
        var s = RubSensor(cell: cell)
        #expect(rubCount(&s, samples: 10, step: 30, period: 3) > 0)
    }

    @Test func rubbingOutsideHeadDoesNothing() {
        var s = RubSensor(cell: cell)
        #expect(rubCount(&s, samples: 50, step: 30, period: 3, inHead: false) == 0)
    }

    @Test func passingStraightAcrossHeadDoesNotCount() {
        // 头宽 14 格 = 56 点, 中速一次穿过去
        var s = RubSensor(cell: cell)
        var triggered = false
        for i in 0..<12 {
            let x = Double(i) * 10
            if s.sample(CGPoint(x: x, y: 0), inHead: x <= 56, dt: 0.1) { triggered = true }
        }
        #expect(!triggered)
    }

    @Test func oneHugeJumpDoesNotCount() {
        var s = RubSensor(cell: cell)
        _ = s.sample(CGPoint(x: 0, y: 0), inHead: false, dt: 0.1)
        let rub = s.sample(CGPoint(x: 2000, y: 0), inHead: true, dt: 0.1)
        #expect(!rub)
    }

    @Test func slowCreepDoesNotCount() {
        var s = RubSensor(cell: cell)
        #expect(rubCount(&s, samples: 200, step: 2, period: 5) == 0)   // 20 点/秒, 比漏掉的还慢
    }

    @Test func stoppingStopsCounting() {
        var s = RubSensor(cell: cell)
        #expect(rubCount(&s, samples: 20, step: 30, period: 3) > 0)
        let p = CGPoint(x: 100, y: 50)
        _ = s.sample(p, inHead: true, dt: 0.1)
        let rub = s.sample(p, inHead: true, dt: 0.1)
        #expect(!rub)
    }
}

struct HeadRectTests {
    let cat = CGRect(x: 100, y: 200, width: 96, height: 96)

    @Test func standFacingRight() {
        // stand 头在第 10 列第 5 行, 14x13 格, 每格 4 点; y 向上
        #expect(headRect(frame: "stand@squint", catRect: cat, cell: 4, dir: 1)
                == CGRect(x: 140, y: 224, width: 56, height: 52))
    }

    @Test func facingLeftMirrors() {
        #expect(headRect(frame: "stand", catRect: cat, cell: 4, dir: -1)
                == CGRect(x: 100, y: 224, width: 56, height: 52))
    }

    @Test func headMovesWithPose() {
        #expect(headRect(frame: "sit", catRect: cat, cell: 4, dir: 1).minX == 128)
        #expect(headRect(frame: "sleep1@half", catRect: cat, cell: 4, dir: 1).maxY == 256)
        #expect(headRect(frame: "knead1", catRect: cat, cell: 4, dir: 1).maxY == 264)
    }
}

struct PettingBrainTests {
    @Test func squintThenHappyThenKnead() {
        let b = awakeCat()
        b.stroke()
        b.tick(dt: dt, platforms: [floorP])
        #expect(b.isPetted)
        #expect(b.frame() == "stand@squint")
        run(b, PetTuning.happyAfter, stroking: true)
        #expect(b.frame() == "stand@happy")
        run(b, PetTuning.kneadAfter - PetTuning.happyAfter, stroking: true)
        #expect(b.currentAnim == "knead")
        #expect(b.frame().hasSuffix("@happy"))
    }

    @Test func noWalkingOrJumpingWhilePetted() {
        let win = Platform(lo: 200, hi: 800, top: 200)
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.7 })  // 0.7 = 选起跳
        run(b, 0.25, on: [floorP, win])                  // 起跳预备中
        #expect(b.currentAnim == "antic")
        let x = b.x
        for _ in 0..<80 {
            b.stroke()
            b.tick(dt: dt, platforms: [floorP, win])
            #expect(!b.isAirborne)
            #expect(b.x == x)
            #expect(b.y == 70)
        }
    }

    @Test func strokeStopsWalking() {
        let b = awakeCat()
        #expect(b.currentAnim == "walk")
        b.stroke()
        let x = b.x
        let seen = run(b, 2, stroking: true)
        #expect(b.x == x)
        #expect(!seen.contains { $0.hasPrefix("walk") })
    }

    @Test func goesBackToDailyLifeAfterRelease() {
        let b = awakeCat()
        run(b, PetTuning.kneadAfter + 1, stroking: true)
        run(b, PetTuning.release - 0.25)
        #expect(b.isPetted)                              // 停一下还不算走
        run(b, 0.5)
        #expect(!b.isPetted)
        #expect(!b.frame().contains("@squint"))
        #expect(!b.frame().contains("@happy"))
        #expect(b.currentAnim != "knead")
        let x = b.x
        run(b, 3)
        #expect(b.x != x)                                // 又走起来了
    }

    @Test func petEyesBeatBlink() {
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.01 })
        let seen = run(b, 3, stroking: true)
        #expect(!seen.dropFirst().contains { $0.hasSuffix("@closed") || $0.hasSuffix("@half") })
    }

    @Test func petSitsIfItWasLicking() {
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.01 })  // 总舔爪
        run(b, 1)
        #expect(b.currentAnim == "lick")
        run(b, 1, stroking: true)
        #expect(b.frame() == "sit@squint")
    }

    // MARK: 吵醒

    @Test func strokingSleeperHalfOpensThenSquintsWithoutGettingUp() {
        let b = sleepingCat()
        let y = b.y
        b.stroke()
        b.tick(dt: dt, platforms: [floorP])
        #expect(b.frame().hasSuffix("@half"))
        #expect(b.currentAnim == "sleep")
        let seen = run(b, PetTuning.stirHalf + 1, stroking: true)
        #expect(b.frame().hasSuffix("@squint"))
        #expect(seen.allSatisfy { $0.hasPrefix("sleep") })
        #expect(b.y == y)
    }

    @Test func sleeperKeepsSleepingAfterRelease() {
        let b = sleepingCat()
        run(b, 2, stroking: true)
        #expect(b.isPetted)
        run(b, PetTuning.release + 0.25)
        #expect(!b.isPetted)
        #expect(b.isSleeping)
        #expect(!b.frame().contains("@"))
        let seen = run(b, 5)
        #expect(seen.allSatisfy { $0.hasPrefix("sleep") })
    }

    // MARK: 摸烦了

    @Test func notFedUpBeforeThreshold() {
        let b = awakeCat()
        let seen = run(b, PetTuning.fedUpAfter - 0.5, stroking: true)
        #expect(!seen.contains { $0.hasPrefix("puff") })
        #expect(b.isPetted)
    }

    @Test func fedUpPuffsThenWalksAway() {
        let b = awakeCat()
        run(b, PetTuning.fedUpAfter + 0.25, stroking: true)
        #expect(!b.isPetted)
        #expect(b.currentAnim == "puff")
        #expect(b.frame().hasPrefix("puff"))
        #expect(b.bubble == "哈!")
        let x = b.x
        let seen = run(b, 7)
        #expect(seen.contains { $0.hasPrefix("walk") })
        #expect(abs(b.x - x) >= PetTuning.walkAwayCells * 4 - 1)
    }

    @Test func cooldownIgnoresStrokesThenRecovers() {
        let b = awakeCat()
        run(b, PetTuning.fedUpAfter + 0.25, stroking: true)
        let seen = run(b, PetTuning.cooldown - 1, stroking: true)
        #expect(!b.isPetted)
        #expect(!seen.contains { $0.contains("@squint") || $0.contains("@happy") })
        run(b, 2)
        b.stroke()
        #expect(b.isPetted)
    }

    @Test func walkAwayStaysOnNarrowPlatform() {
        let ledge = Platform(lo: 280, hi: 320, top: 70)    // 比猫还窄, 没地方走
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.5 })
        run(b, PetTuning.fedUpAfter + 8, stroking: true, on: [ledge])
        #expect(!b.isFalling)
        #expect(b.x == 300)
    }

    @Test func releaseResetsTheFedUpClock() {
        let b = awakeCat()
        for _ in 0..<3 {
            let seen = run(b, PetTuning.fedUpAfter * 0.6, stroking: true)
            #expect(!seen.contains { $0.hasPrefix("puff") })
            run(b, PetTuning.release + 0.25)
        }
    }

    // MARK: 空中与打断

    @Test func airborneIgnoresStrokesAndRespondsAfterLanding() {
        let win = Platform(lo: 100, hi: 600, top: 500)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        run(b, 0.25, on: [floorP, win])
        b.tick(dt: dt, platforms: [floorP])              // 窗口被拖走
        #expect(b.isFalling)
        b.stroke()
        #expect(!b.isPetted)
        var seen: [String] = []
        while b.isFalling {
            b.stroke()
            b.tick(dt: dt, platforms: [floorP])
            seen.append(b.frame())
        }
        #expect(!seen.contains { $0.contains("@") })
        b.stroke()
        #expect(!b.isPetted)                             // 落地动作演完才理你
        run(b, 0.625, on: [floorP])
        b.stroke()
        #expect(b.isPetted)
    }

    @Test func midJumpIgnoresStrokes() {
        let win = Platform(lo: 200, hi: 800, top: 200)
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.7 })
        var guardN = 0
        while !b.isAirborne && guardN < 40 {
            b.tick(dt: dt, platforms: [floorP, win])
            guardN += 1
        }
        #expect(b.isAirborne)
        b.stroke()
        #expect(!b.isPetted)
    }

    @Test func windowRemovedWhilePettedFallsCleanly() {
        let win = Platform(lo: 100, hi: 600, top: 500)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 0, rand: { 0.5 })
        run(b, 1, stroking: true, on: [floorP, win])
        #expect(b.isPetted)
        b.tick(dt: dt, platforms: [floorP])
        #expect(b.isFalling)
        #expect(!b.isPetted)
        #expect(b.frame() == "fall")
    }

    @Test func pokeInterruptsPetting() {
        let b = awakeCat()
        run(b, 1, stroking: true)
        b.poke()
        b.tick(dt: dt, platforms: [floorP])
        #expect(!b.isPetted)
        #expect(b.currentAnim == "puff")
        b.stroke()                                       // 正炸毛, 摸也没用
        #expect(!b.isPetted)
    }

    @Test func pokeDoesNotStartCooldown() {
        let b = awakeCat()
        b.poke()
        run(b, 10)
        b.stroke()
        #expect(b.isPetted)
    }
}
