import Testing
import CoreGraphics
@testable import PerchCatCore

// 用 swift-testing (本机只有 CommandLineTools, 没有 XCTest). 下面是薄封装, 保持断言可读.
func XCTAssertEqual<T: Equatable>(_ a: T, _ b: T, sourceLocation: SourceLocation = #_sourceLocation) { #expect(a == b, sourceLocation: sourceLocation) }
func XCTAssertEqual(_ a: Double, _ b: Double, accuracy: Double, sourceLocation: SourceLocation = #_sourceLocation) { #expect(abs(a - b) <= accuracy, sourceLocation: sourceLocation) }
func XCTAssertTrue(_ a: Bool, sourceLocation: SourceLocation = #_sourceLocation) { #expect(a, sourceLocation: sourceLocation) }
func XCTAssertFalse(_ a: Bool, sourceLocation: SourceLocation = #_sourceLocation) { #expect(!a, sourceLocation: sourceLocation) }
func XCTAssertNil<T>(_ a: T?, sourceLocation: SourceLocation = #_sourceLocation) { #expect(a == nil, sourceLocation: sourceLocation) }
func XCTAssertThrowsError<T>(_ f: @autoclosure () throws -> T, sourceLocation: SourceLocation = #_sourceLocation) {
    #expect(throws: (any Error).self, sourceLocation: sourceLocation) { _ = try f() }
}

struct CoreTests {
    @Test func testCGToAppKit() {
        // 主屏高 1000, CG 里 y=100 高 300 的窗口 -> AppKit 底边 600, 顶边 900
        let r = cgToAppKit(CGRect(x: 50, y: 100, width: 400, height: 300), primaryHeight: 1000)
        XCTAssertEqual(r, CGRect(x: 50, y: 600, width: 400, height: 300))
        XCTAssertEqual(r.maxY, 900)
    }

    @Test func testSupport() {
        let ps = [Platform(lo: 0, hi: 1000, top: 70), Platform(lo: 100, hi: 600, top: 700)]
        XCTAssertEqual(supportAt(ps, x: 300, y: 700)?.top, 700)
        XCTAssertEqual(supportAt(ps, x: 300, y: 702)?.top, 700)   // 容差内
        XCTAssertNil(supportAt(ps, x: 700, y: 700))               // 站的窗口没了/挪开
        XCTAssertEqual(supportAt(ps, x: 700, y: 70)?.top, 70)
    }

    @Test func testFallLandsOnWindowBelowNotFloor() {
        let ps = [Platform(lo: 0, hi: 1000, top: 70), Platform(lo: 0, hi: 400, top: 300)]
        var y = 700.0, vy = 0.0, landed: Platform?
        for _ in 0..<200 {
            let r = fallStep(x: 200, y: y, vy: vy, dt: 0.125, g: 1360, platforms: ps)
            y = r.y; vy = r.vy; landed = r.landed
            if landed != nil { break }
        }
        XCTAssertEqual(landed?.top, 300)
        XCTAssertEqual(y, 300)
    }

    @Test func testFallToFloorWhenNothingBelow() {
        let ps = [Platform(lo: 0, hi: 1000, top: 70)]
        var y = 700.0, vy = 0.0, landed: Platform?
        for _ in 0..<200 {
            let r = fallStep(x: 900, y: y, vy: vy, dt: 0.125, g: 1360, platforms: ps)
            y = r.y; vy = r.vy; landed = r.landed
            if landed != nil { break }
        }
        XCTAssertEqual(landed?.top, 70)
    }

    @Test func testArcApexIsClearanceAboveHigherEnd() {
        let a = Arc(x0: 0, y0: 100, tx: 300, ty: 400, clearance: 24, g: 1360)
        var maxY = -Double.infinity
        var t = 0.0
        while t < a.T { maxY = max(maxY, a.pos(t).y); t += 0.001 }
        XCTAssertEqual(maxY, 424, accuracy: 0.5)
        XCTAssertEqual(a.pos(a.T).y, 400, accuracy: 1e-6)
        XCTAssertEqual(a.pos(a.T).x, 300, accuracy: 1e-6)
        // 往下跳: 顶点比起点高 24
        let d = Arc(x0: 0, y0: 400, tx: 300, ty: 100, clearance: 24, g: 1360)
        XCTAssertEqual(d.pos(d.T).y, 100, accuracy: 1e-6)
        XCTAssertEqual(d.vy, (2 * 1360 * 24).squareRoot(), accuracy: 1e-9)
    }

    @Test func testPickJump() {
        let floor = Platform(lo: 0, hi: 1440, top: 70)
        let win = Platform(lo: 500, hi: 900, top: 300)
        let far = Platform(lo: 3000, hi: 3400, top: 300)
        let ps = [floor, win, far]
        let j = pickJump(from: 100, y: 70, platforms: ps, halfWidth: 40, maxDX: 500, maxRise: 400, r1: 0, r2: 0.5)
        XCTAssertEqual(j?.platform, win)                 // 脚下的地板不算, 太远的不算
        XCTAssertEqual(j!.tx, 540, accuracy: 1e-9)       // 夹进 [lo+半宽, hi-半宽]
        XCTAssertNil(pickJump(from: 100, y: 70, platforms: ps, halfWidth: 40, maxDX: 200, maxRise: 400, r1: 0, r2: 0))
        XCTAssertNil(pickJump(from: 100, y: 70, platforms: ps, halfWidth: 40, maxDX: 500, maxRise: 100, r1: 0, r2: 0))
    }

    @Test func testBrainFallsWhenWindowRemoved() {
        let floor = Platform(lo: 0, hi: 1440, top: 70)
        let win = Platform(lo: 100, hi: 600, top: 500)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        for _ in 0..<10 { b.tick(dt: 0.125, platforms: [floor, win]) }
        XCTAssertFalse(b.isFalling)
        XCTAssertEqual(b.y, 500)
        b.tick(dt: 0.125, platforms: [floor])            // 窗口被拖走
        XCTAssertTrue(b.isFalling)
        XCTAssertEqual(b.frame(), "fall")
        for _ in 0..<20 { b.tick(dt: 0.125, platforms: [floor]) }
        XCTAssertFalse(b.isFalling)
        XCTAssertEqual(b.y, 70)
    }

    @Test func testBrainFallsOntoLowerWindow() {
        let floor = Platform(lo: 0, hi: 1440, top: 70)
        let hi = Platform(lo: 100, hi: 600, top: 500)
        let lo = Platform(lo: 0, hi: 700, top: 250)
        let b = CatBrain(scale: 4, x: 300, y: 500, sleepChance: 1, rand: { 0.5 })
        b.tick(dt: 0.125, platforms: [floor, hi, lo])
        b.tick(dt: 0.125, platforms: [floor, lo])
        for _ in 0..<20 { b.tick(dt: 0.125, platforms: [floor, lo]) }
        XCTAssertEqual(b.y, 250)
    }

    @Test func testPokePuffsThenLicks() {
        let floor = Platform(lo: 0, hi: 1440, top: 70)
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 1, rand: { 0.5 })
        for _ in 0..<5 { b.tick(dt: 0.125, platforms: [floor]) }
        b.poke()
        b.tick(dt: 0.125, platforms: [floor])
        XCTAssertEqual(b.currentAnim, "puff")
        XCTAssertEqual(b.bubble, "哈!")
        for _ in 0..<20 { b.tick(dt: 0.125, platforms: [floor]) }
        XCTAssertNil(b.bubble)
        XCTAssertTrue(["sit", "lick"].contains(b.currentAnim))
    }

    @Test func testBlinkSequence() {
        let floor = Platform(lo: 0, hi: 1440, top: 70)
        let b = CatBrain(scale: 4, x: 300, y: 70, sleepChance: 0, rand: { 0.01 })  // 总选舔爪/不睡
        var seen: [String] = []
        for _ in 0..<400 {
            b.tick(dt: 0.125, platforms: [floor])
            seen.append(b.frame())
        }
        XCTAssertTrue(seen.contains { $0.hasSuffix("@half") })
    }

    @Test func testSkinParseAndResolve() throws {
        let json = ##"{"palette":{"K":"#000000","W":"#ffffff"},"poses":{"stand":["KW","..",".."],"stand@half":["WK","..",".."]}}"##
        let s = try Skin.parse(json: json)
        XCTAssertEqual(s.width, 2)
        XCTAssertEqual(s.footInset, 2)
        XCTAssertEqual(s.resolve("stand@half")[0], "WK")
        XCTAssertEqual(s.resolve("walk3@closed")[0], "KW")   // 缺失退回 stand
        XCTAssertEqual(Array(s.rgba(s.resolve("stand"))[0..<8]), [0, 0, 0, 255, 255, 255, 255, 255])
        XCTAssertThrowsError(try Skin.load(path: "/nonexistent/skin.json"))
        XCTAssertThrowsError(try Skin.parse(json: "{}"))
    }
}
