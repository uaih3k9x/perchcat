import Foundation

/// 动画表: 名字 -> 帧序列. 不在表里的名字就是单帧静态姿势.
let anims: [String: (frames: [String], beat: Double, once: Bool)] = [
    "walk": (["walk0", "walk1", "walk2", "walk3", "walk4", "walk5", "walk6", "walk7"], 0.125, false),
    "antic": (["half", "crouch", "crouch", "crouch"], 0.125, true),
    "landing": (["land", "land", "land", "half", "half"], 0.1, true),
    "sleep": (["sleep0", "sleep1"], 1.2, false),
    "knead": (["knead0", "knead1"], 0.5, false),
    "puff": (["puff0", "puff1"], 0.25, false),
    "lick": (["lick0", "lick1", "lick2", "lick1"], 0.25, false),
    "shake": (["shake0", "shake1"], 0.125, false),
]
let noBlink: Set<String> = ["sleep", "knead", "puff", "lick", "landing", "shake"]

enum Step {
    case hold(String, Double)   // 动画名, 秒
    case walk(Double)           // 走到 x
    case jump(Double, Double)   // 跳到 (x, y)
    case eye(String?)           // 换表情 (瞬时)
    case say(String?)           // 气泡文字 (瞬时)
}

/// 猫的小状态机. 步骤队列 + 当前步骤; 队列空了就 plan() 下一个活动.
/// 每次 tick 传入当前平台列表, 脚下平台没了 (窗口被拖走/关掉) 就自由落体.
public final class CatBrain {
    public let scale: Double
    public var sleepChance: Double
    public var crossChance = PlatformTuning.crossChance
    public var screens: [CGRect] = []       // 全部屏幕; 空 = 不限制跳跃路线
    public private(set) var x: Double
    public private(set) var y: Double
    public private(set) var dir: Double = 1
    public private(set) var bubble: String?
    public private(set) var isFalling = false

    private var rand: () -> Double
    private var queue: [Step] = []
    private var cur: Step?
    private var t = 0.0
    private var arc: Arc?
    private var anim = "stand"
    private var animT = 0.0
    private var eye: String?
    private var blinkLeft = 3.0
    private var blinkSeq: [String] = []
    private var blinkT = 0.0
    private var vy = 0.0
    private var vx = 0.0
    private var petting: Petting?
    private var cooldownLeft = 0.0
    private var lift: LiftMotion?
    private var fellAsleep = false      // 睡着时脚下没了: 落地抖抖毛接着睡

    private struct Petting {
        var elapsed = 0.0
        var sinceStroke = 0.0
        let asleep: Bool        // 睡着被摸: 只动眼睛, 醒不来, 原来的睡眠步骤接着算
        let pose: String        // 醒着被摸时的姿势, 踩奶之前不变
    }

    // 以下参数随 scale 变 (原版 scale=5 时 g=1700, 走速 56)
    public var gravity: Double { 340 * scale }
    public var walkSpeed: Double { 11.2 * scale }
    public var halfWidth: Double { 10 * scale }
    public let clearance = 24.0

    public init(scale: Double, x: Double, y: Double, sleepChance: Double = 0.55,
                rand: @escaping () -> Double = { Double.random(in: 0..<1) }) {
        self.scale = scale; self.x = x; self.y = y
        self.sleepChance = sleepChance; self.rand = rand
    }

    public var isSleeping: Bool { anim == "sleep" && !isFalling }
    public var isAirborne: Bool { isFalling || arc != nil || isLifted }
    public var isJumping: Bool { arc != nil }
    public var currentAnim: String { anim }
    public var isPetted: Bool { petting != nil }
    public var isLifted: Bool { lift != nil }

    /// 当前该画的帧名, 如 "walk3" 或 "stand@half".
    public func frame() -> String {
        var base = anim
        if let a = anims[anim] {
            let i = Int((animT + 1e-6) / a.beat)
            base = a.frames[a.once ? min(i, a.frames.count - 1) : i % a.frames.count]
        }
        if let e = blinkSeq.first ?? eye { return base + "@" + e }
        return base
    }

    public func poke() {
        guard !isAirborne else { return }
        petting = nil
        queue = [.eye(nil), .say("哈!"), .hold("puff", 1.6), .say(nil)] + lick()
        cur = nil
    }

    /// 鼠标正在头上蹭. 空中、落地动作、炸毛、冷却期都不理.
    public func stroke() {
        guard !isAirborne, cooldownLeft <= 0, !["fall", "landing", "puff", "shake"].contains(anim) else { return }
        if petting == nil {
            let asleep = anim == "sleep"
            petting = Petting(asleep: asleep, pose: ["sit", "lick"].contains(anim) ? "sit" : anim == "knead" ? "knead" : "stand")
            if !asleep { queue = []; cur = nil }
            bubble = nil; blinkSeq = []
            applyPetLook()
        }
        petting?.sinceStroke = 0
    }

    // MARK: 拎

    /// 被拎起来: 不管正在干什么都停下. 拎着时 (x, y) 就是鼠标位置, frame() 是 fall (没有拎起帧时直接画它).
    public func beginLift(x: Double, y: Double) {
        petting = nil; eye = nil; bubble = nil; blinkSeq = []
        queue = []; cur = nil; arc = nil
        isFalling = false; vy = 0; vx = 0; fellAsleep = false
        setAnim("fall")
        self.x = x; self.y = y
        lift = LiftMotion(x: x, cell: scale)
    }

    public func liftTo(x: Double, y: Double) {
        guard isLifted else { return }
        self.x = x; self.y = y
    }

    /// 松手: (x, y) 是落下起点的脚底位置, (vx, vy) 是甩出去的初速度.
    public func release(x: Double, y: Double, vx: Double = 0, vy: Double = 0) {
        guard isLifted else { return }
        lift = nil
        self.x = x; self.y = y
        startFall()
        self.vx = vx; self.vy = vy
    }

    public func liftPose(stages: Int) -> LiftPose? { lift?.pose(stages: stages) }

    /// 脚下的窗口挪了: 猫跟着挪, 正要走去的目标也一起挪. 睡着的不跟, 脚下空了就掉下去.
    public func carry(dx: Double, dy: Double) {
        guard !isAirborne, !isSleeping else { return }
        x += dx; y += dy
        func shifted(_ s: Step) -> Step { if case .walk(let tx) = s { return .walk(tx + dx) }; return s }
        cur = cur.map(shifted)
        queue = queue.map(shifted)
    }

    public func tick(dt rawDt: Double, platforms: [Platform]) {
        let dt = min(max(rawDt, 0), 0.6)
        animT += dt
        cooldownLeft = max(0, cooldownLeft - dt)
        if lift != nil { lift?.update(x: x, dt: dt); return }
        updateBlink(dt)
        if arc == nil { liftOntoFloor(platforms) }      // 先看地板: 否则猫在地板下面会被 rescue 直接瞬移上去
        if arc == nil { rescueIfLost(platforms) }
        if isFalling { fall(dt, platforms); return }
        if arc == nil {
            if let s = supportAt(platforms, x: x, y: y) { y = s.top }
            else { startFall(); fall(dt, platforms); return }
        }
        if isPetted {
            updatePetting(dt, platforms)
            if isPetted { return }
        }
        var guardN = 0
        while cur == nil && guardN < 20 {
            guardN += 1
            if queue.isEmpty { plan(platforms) }
            begin(queue.removeFirst())
        }
        advance(dt)
    }

    // MARK: 下落

    private func startFall() {
        fellAsleep = anim == "sleep"
        if isPetted { eye = nil; petting = nil }
        queue = []; cur = nil; arc = nil; bubble = nil; blinkSeq = []
        isFalling = true; vy = 0; vx = 0; setAnim("fall")
    }

    private func fall(_ dt: Double, _ ps: [Platform]) {
        let r = fallStep(x: x, y: y, vy: vy, dt: dt, g: gravity, platforms: ps,
                         vx: vx, halfWidth: halfWidth, screens: screens)
        x = r.x; y = r.y; vy = r.vy; vx = r.vx
        if r.landed != nil { land() }
    }

    private func land() {
        isFalling = false; vy = 0; vx = 0
        queue = [.hold("landing", 0.5)] + (fellAsleep ? [.hold("shake", 1)] + backToSleep() : [.hold("stand", 0.5)])
        fellAsleep = false
    }

    private func backToSleep() -> [Step] { [.hold("sit", 0.5), .hold("sleep", 20 + rand() * 40)] }

    /// 脚下再也不会有东西接住 (屏幕被拔、布局变了): 放回最近的地板.
    private func rescueIfLost(_ ps: [Platform]) {
        guard !ps.contains(where: { $0.lo <= x && x <= $0.hi && $0.top <= y + 3 }) else { return }
        let floors = ps.filter { $0.windowID == nil }
        guard let f = (floors.isEmpty ? ps : floors).min(by: { gap(x, $0) < gap(x, $1) }) else { return }
        x = min(max(x, f.lo + halfWidth), f.hi - halfWidth); y = f.top
        if isPetted { eye = nil; petting = nil }
        isFalling = false; vy = 0; vx = 0; queue = [.hold("stand", 0.5)]; cur = nil; bubble = nil
    }

    /// 地板升起来了 (Dock 回来了): 猫跳上去, 差一点点就直接站上去. 正在下落的直接接住.
    private func liftOntoFloor(_ ps: [Platform]) {
        guard let f = ps.first(where: { p in
            guard let base = p.base else { return false }
            return p.lo <= x && x <= p.hi && base <= y && y < p.top
        }) else { return }
        if isFalling { y = f.top; land(); return }
        if f.top - y <= 3 { y = f.top; return }
        let asleep = anim == "sleep"
        if isPetted { eye = nil; petting = nil }
        bubble = nil
        queue = [.hold("landing", 0.5)] + (asleep ? backToSleep() : [.hold("stand", 0.5)])
        begin(.jump(x, f.top))
    }

    // MARK: 摸

    private func applyPetLook() {
        guard let p = petting else { return }
        if p.asleep {
            eye = p.elapsed < PetTuning.stirHalf ? "half" : "squint"
        } else {
            eye = p.elapsed < PetTuning.happyAfter ? "squint" : "happy"
            setAnim(p.elapsed < PetTuning.kneadAfter ? p.pose : "knead")
        }
    }

    private func updatePetting(_ dt: Double, _ ps: [Platform]) {
        guard var p = petting else { return }
        p.elapsed += dt
        p.sinceStroke += dt
        petting = p
        if p.sinceStroke >= PetTuning.release - 1e-6 {
            petting = nil; eye = nil
            if !p.asleep { queue = [.hold("stand", 0.5)] }
        } else if p.elapsed >= PetTuning.fedUpAfter - 1e-6 {
            petting = nil; eye = nil; cur = nil
            cooldownLeft = PetTuning.cooldown
            queue = [.say("哈!"), .hold("puff", PetTuning.puffSeconds), .say(nil)] + walkAway(ps)
        } else {
            applyPetLook()
        }
    }

    /// 往平台上空得多的那边走. 平台太窄就不走.
    private func walkAway(_ ps: [Platform]) -> [Step] {
        guard let s = supportAt(ps, x: x, y: y) else { return [] }
        let lo = s.lo + halfWidth, hi = s.hi - halfWidth
        guard hi > lo else { return [] }
        let d = PetTuning.walkAwayCells * scale
        return [.walk(x - lo > hi - x ? max(lo, x - d) : min(hi, x + d)), .hold("stand", 0.5)]
    }

    // MARK: 步骤

    private func setAnim(_ a: String) {
        if anim != a { anim = a; animT = 0 }
        if noBlink.contains(a) { blinkSeq = [] }
    }

    private func begin(_ s: Step) {
        cur = s; t = 0
        switch s {
        case .hold(let a, _): setAnim(a)
        case .walk(let tx):
            setAnim("walk")
            if abs(tx - x) > 0.5 { dir = tx > x ? 1 : -1 }
        case .jump(let tx, let ty):
            if abs(tx - x) > 1 { dir = tx > x ? 1 : -1 }
            arc = Arc(x0: x, y0: y, tx: tx, ty: ty, clearance: clearance, g: gravity)
            setAnim("rise")
        case .eye(let e): eye = e; cur = nil
        case .say(let b): bubble = b; cur = nil
        }
    }

    private func advance(_ dt: Double) {
        guard let s = cur else { return }
        t += dt
        switch s {
        case .hold(_, let sec): if t >= sec - 1e-6 { cur = nil }
        case .walk(let tx):
            let step = min(walkSpeed * dt, abs(tx - x))
            x += dir * step
            if abs(tx - x) <= 0.5 { cur = nil }
        case .jump(let tx, let ty):
            guard let a = arc else { cur = nil; return }
            if t >= a.T { x = tx; y = ty; arc = nil; cur = nil }
            else { let p = a.pos(t); x = p.x; y = p.y; setAnim(a.pose(t)) }
        default: cur = nil
        }
    }

    private func updateBlink(_ dt: Double) {
        if !blinkSeq.isEmpty {
            blinkT += dt
            if blinkT >= 0.1 { blinkT = 0; blinkSeq.removeFirst() }
            return
        }
        blinkLeft -= dt
        if blinkLeft <= 0 {
            blinkLeft = 2 + rand() * 4
            if !noBlink.contains(anim), eye == nil { blinkSeq = ["half", "closed"]; blinkT = 0 }
        }
    }

    // MARK: 规划

    private func pick<T>(_ a: [T]) -> T { a[min(Int(rand() * Double(a.count)), a.count - 1)] }

    private func lick() -> [Step] {
        [.hold("sit", 0.5), .eye("half"), .hold("lick", 3 + rand()), .hold("sit", 0.5), .eye(nil), .hold("stand", 0.5)]
    }

    /// 选一次跳: 平时只在当前屏; 偶尔 (crossChance) 换屏, 可能先走到平台边上再过去.
    /// 路线必须一直在屏幕上, 所以只会从相邻且有重叠的边过去.
    private func jumpSteps(_ ps: [Platform], support: Platform?) -> [Step]? {
        func route(from: Double, otherScreen: Bool) -> [Step]? {
            let here = screenIndex(x, y, screens)
            guard let j = pickJump(from: from, y: y, platforms: ps, halfWidth: halfWidth,
                                   maxDX: 60 * scale, maxRise: 90 * scale, r1: rand(), r2: rand(),
                                   allowed: { [self] p, tx in
                (here != screenIndex(tx, p.top, screens)) == otherScreen &&
                    arcStaysOnScreens(Arc(x0: from, y0: y, tx: tx, ty: p.top, clearance: clearance, g: gravity), screens: screens)
            }) else { return nil }
            let walk: [Step] = from == x ? [] : [.walk(from)]
            return walk + [.hold("antic", 0.5), .jump(j.tx, j.platform.top), .hold("landing", 0.5), .hold("stand", 0.5)]
        }
        if rand() < crossChance {
            var starts = [x]
            if let s = support, s.width > 2 * halfWidth { starts += [s.hi - halfWidth, s.lo + halfWidth] }
            if let r = route(from: pick(starts), otherScreen: true) { return r }
        }
        return route(from: x, otherScreen: false)
    }

    private func plan(_ ps: [Platform]) {
        let wasSleeping = anim == "sleep"
        if rand() < sleepChance {
            let dur = 20 + rand() * 40
            queue = wasSleeping ? [.hold("sleep", dur)]
                                : [.hold("stand", 0.5), .hold("sit", 0.5), .hold("sleep", dur)]
            return
        }
        var q: [Step] = wasSleeping ? [.hold("sit", 0.5), .hold("stand", 0.3)] : []
        let r = rand()
        let sup = supportAt(ps, x: x, y: y)
        if r < 0.25 {
            q += lick()
        } else if r < 0.55, let s = sup, s.width > 4 * halfWidth {
            let lo = s.lo + halfWidth, hi = s.hi - halfWidth
            let far = x - lo > hi - x ? lo : hi
            q += [.eye(pick([nil, nil, "half"])), .walk(far), .hold("stand", 0.5),
                  .walk(lo + (hi - lo) * (0.3 + rand() * 0.4)), .eye(nil), .hold("stand", 0.5)]
        } else if r < 0.85, let steps = jumpSteps(ps, support: sup) {
            q += steps
        } else {
            q += [.hold("stand", 0.5), .eye(pick([nil, nil, "half", "down"])), .hold("knead", 5),
                  .eye(nil), .hold("stand", 0.5)]
        }
        queue = q
    }
}
