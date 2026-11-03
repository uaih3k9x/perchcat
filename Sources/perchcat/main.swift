import AppKit
import PerchCatCore

// 用法: perchcat [--skin 路径] [--scale 4] [--sleep-chance 0.55] [--run-seconds N]
//       调试: [--trace] [--start x,y] [--cross-chance 0.5]
// 环境变量: PERCHCAT_SKIN, PERCHCAT_SCALE.

var signalSources: [DispatchSourceSignal] = []
let defaultSkin = defaultSkinPath(home: NSHomeDirectory())

func arg(_ name: String) -> String? {
    let a = CommandLine.arguments
    guard let i = a.firstIndex(of: name), i + 1 < a.count else { return nil }
    return a[i + 1]
}

let skinPath = chooseSkin(explicit: arg("--skin") ?? ProcessInfo.processInfo.environment["PERCHCAT_SKIN"],
                          defaultPath: defaultSkin, exists: { FileManager.default.fileExists(atPath: $0) })
let scale = Double(arg("--scale") ?? ProcessInfo.processInfo.environment["PERCHCAT_SCALE"] ?? "") ?? 4
let sleepChance = Double(arg("--sleep-chance") ?? "") ?? 0.55
let runSeconds = Double(arg("--run-seconds") ?? "")
let crossChance = Double(arg("--cross-chance") ?? "") ?? PlatformTuning.crossChance
let trace = CommandLine.arguments.contains("--trace")
let startPos = arg("--start")?.split(separator: ",").compactMap { Double($0) }

let skin: Skin
do { skin = try skinPath.map { try Skin.load(path: $0) } ?? Skin.parse(json: builtinSkinJSON) } catch {
    FileHandle.standardError.write(Data("perchcat: \(error)\n".utf8))
    exit(1)
}

/// 拎起帧在皮肤同目录. 没有就退回现有姿势, 坏了也不影响猫.
let shakeSheet: ShakeSheet? = {
    do {
        guard let skinPath else { return try ShakeSheet.parse(Data(builtinShakeJSON.utf8)) }
        return try ShakeSheet.load(path: ShakeSheet.path(besideSkin: skinPath))
    } catch {
        fputs("抖毛表读取失败, 退回眯眼坐着: \(error)\n", stderr)
        return nil
    }
}()

let scruff: ScruffSheet? = {
    do {
        guard let skinPath else { return try ScruffSheet.parse(Data(builtinScruffJSON.utf8)) }
        return try ScruffSheet.load(path: ScruffSheet.path(besideSkin: skinPath))
    } catch {
        FileHandle.standardError.write(Data("perchcat: \(error)\n".utf8))
        return nil
    }
}()

enum MouseEvent { case down, drag, up }

/// 按下才收鼠标的视图. mouseDown 之后 AppKit 把拖动和松开都送回这个视图, 鼠标跑到别的屏也一样.
final class CatView: NSView {
    var onMouse: ((MouseEvent) -> Void)?
    override var acceptsFirstResponder: Bool { false }
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { onMouse?(.down) }
    override func mouseDragged(with event: NSEvent) { onMouse?(.drag) }
    override func mouseUp(with event: NSEvent) { onMouse?(.up) }
}

/// 一块屏一个全屏透明窗口. 实测 (跨屏实验): "显示器具有单独的空间"开着时, 跨屏窗口只显示在
/// 占面积大的那块屏, 另一边被切掉. 所以每块屏各画一只猫, 过边界时两边各露一半.
final class Overlay {
    let frame: CGRect
    let backing: CGFloat
    let window: NSWindow
    let view = CatView()
    let catLayer = CALayer()
    let bubbleLayer = CATextLayer()
    var shownFrame = ""

    init(screen s: NSScreen, catW: Double, catH: Double, onMouse: @escaping (MouseEvent) -> Void) {
        frame = s.frame
        backing = s.backingScaleFactor
        window = NSWindow(contentRect: s.frame, styleMask: .borderless, backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.level = .statusBar
        window.ignoresMouseEvents = true
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        view.wantsLayer = true
        view.layer?.addSublayer(catLayer)
        catLayer.magnificationFilter = .nearest
        catLayer.minificationFilter = .nearest
        catLayer.bounds = CGRect(x: 0, y: 0, width: catW, height: catH)
        catLayer.contentsScale = backing
        catLayer.isHidden = true
        bubbleLayer.fontSize = 14
        bubbleLayer.alignmentMode = .center
        bubbleLayer.foregroundColor = NSColor.black.cgColor
        bubbleLayer.backgroundColor = NSColor.white.cgColor
        bubbleLayer.cornerRadius = 8
        bubbleLayer.borderColor = NSColor.black.cgColor
        bubbleLayer.borderWidth = 1
        bubbleLayer.contentsScale = backing
        bubbleLayer.isHidden = true
        view.layer?.addSublayer(bubbleLayer)
        view.onMouse = onMouse
        window.contentView = view
        window.setFrame(s.frame, display: false)
        window.orderFrontRegardless()
    }

    func close() { window.orderOut(nil); window.close() }
}

final class Controller: NSObject, NSMenuDelegate {
    var overlays: [Overlay] = []
    let brain: CatBrain
    var platforms: [Platform] = []
    var lastWindows: [WindowInfo] = []
    var bundleIDs: [Int32: String] = [:]
    var lastRefresh = Date.distantPast
    var followUntil = Date.distantPast      // 脚下窗口刚动过: 这之前高频刷新, 拖窗口时跟手
    var lastTick = Date()
    var lastTrace = Date.distantPast
    var timer: Timer?
    var slow = false
    var mouseTimer: Timer?
    var rubSensor = RubSensor(cell: scale)
    var lastMouseCheck = Date()
    var images: [String: CGImage] = [:]
    var statusItem: NSStatusItem?
    var hideItem: NSMenuItem?
    var autoStartItem: NSMenuItem?
    var hidden = false
    let agent = LaunchAgent()
    var gesture = PressGesture()
    var tracker = ThrowTracker()
    var upMisses = 0                        // 连续几次看到键已松开但还没收到 mouseUp
    let ownPID = ProcessInfo.processInfo.processIdentifier
    var catW: Double { Double(skin.width) * scale }
    var catH: Double { Double(skin.height) * scale }

    override init() {
        let s = NSScreen.screens[0]
        let start = startPos?.count == 2 ? startPos! : [s.frame.midX, s.visibleFrame.minY]
        brain = CatBrain(scale: scale, x: start[0], y: start[1], sleepChance: sleepChance)
        brain.crossChance = crossChance
        super.init()
        refreshPlatforms()
        schedule(slow: false)
        startMouseTimer()
        makeStatusItem()
    }

    func startMouseTimer() {
        lastMouseCheck = Date()
        mouseTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in self?.checkMouse() }
        mouseTimer?.tolerance = 0.05
    }

    func makeStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let img = NSImage(systemSymbolName: "cat.fill", accessibilityDescription: "perchcat")
            ?? NSImage(systemSymbolName: "pawprint.fill", accessibilityDescription: "perchcat") {
            item.button?.image = img
        } else { item.button?.title = "猫" }
        let menu = NSMenu()
        menu.delegate = self
        func add(_ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
            let m = NSMenuItem(title: title, action: action, keyEquivalent: key)
            m.target = self
            menu.addItem(m)
            return m
        }
        hideItem = add("藏起来", #selector(toggleHidden))
        autoStartItem = add("开机自启", #selector(toggleAutoStart))
        menu.addItem(.separator())
        _ = add("退出", #selector(quit), key: "q")
        item.menu = menu
        statusItem = item
    }

    /// 每次打开菜单都按实际状态刷新, 勾选以磁盘上的 plist 为准.
    func menuNeedsUpdate(_ menu: NSMenu) {
        hideItem?.title = hidden ? "显示" : "藏起来"
        autoStartItem?.state = agent.isInstalled ? .on : .off
    }

    @objc func quit() { NSApp.terminate(nil) }

    @objc func toggleHidden() {
        hidden.toggle()
        if hidden {
            timer?.invalidate(); timer = nil
            mouseTimer?.invalidate(); mouseTimer = nil
            for o in overlays {
                o.catLayer.isHidden = true
                o.bubbleLayer.isHidden = true
                o.window.ignoresMouseEvents = true
            }
        } else {
            refreshPlatforms()
            schedule(slow: false)
            startMouseTimer()
            render()
        }
    }

    /// 只有用户在菜单里勾选才写 plist, 取消就删. 指向正在跑的这个程序和这份皮肤.
    @objc func toggleAutoStart() {
        do {
            if agent.isInstalled {
                try agent.remove()
            } else {
                let exe = Bundle.main.executablePath ?? CommandLine.arguments[0]
                try agent.install(executable: URL(fileURLWithPath: exe).standardizedFileURL.path,
                                  skinPath: skinPath.map { URL(fileURLWithPath: $0).standardizedFileURL.path })
            }
        } catch {
            FileHandle.standardError.write(Data("perchcat: 开机自启设置失败: \(error)\n".utf8))
        }
    }

    // MARK: 屏幕和平台

    /// 屏幕增减/挪动/改分辨率: 重建每屏的窗口. 定时调用, 不写死布局也不依赖通知.
    func syncOverlays() {
        let now = NSScreen.screens
        guard !now.isEmpty else { return }
        let same = now.count == overlays.count && zip(now, overlays).allSatisfy {
            $0.frame == $1.frame && $0.backingScaleFactor == $1.backing
        }
        if same { return }
        overlays.forEach { $0.close() }
        overlays = now.map { Overlay(screen: $0, catW: catW, catH: catH) { [weak self] in self?.mouse($0) } }
        brain.screens = now.map(\.frame)
    }

    /// 只读位置、层级、透明度、所属进程和窗口编号. 不碰标题, 不需要屏幕录制权限.
    func readWindows() -> [WindowInfo] {
        let primaryH = Double(NSScreen.screens[0].frame.height)
        let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        return list.compactMap { d in
            guard (d[kCGWindowLayer as String] as? Int) == 0,
                  let b = d[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: b as CFDictionary) else { return nil }
            let pid = Int32((d[kCGWindowOwnerPID as String] as? Int) ?? 0)
            return WindowInfo(rect: cgToAppKit(r, primaryHeight: primaryH), layer: 0,
                              alpha: (d[kCGWindowAlpha as String] as? Double) ?? 1, pid: pid,
                              id: (d[kCGWindowNumber as String] as? Int) ?? 0, bundleID: bundleID(pid))
        }
    }

    func bundleID(_ pid: Int32) -> String {
        if let b = bundleIDs[pid] { return b }
        let b = NSRunningApplication(processIdentifier: pid)?.bundleIdentifier ?? ""
        bundleIDs[pid] = b
        return b
    }

    func refreshPlatforms() {
        lastRefresh = Date()
        syncOverlays()
        let ws = readWindows()
        if !brain.isAirborne, !brain.isSleeping, let id = supportAt(platforms, x: brain.x, y: brain.y)?.windowID,
           let d = carryDelta(windowID: id, old: lastWindows, new: ws) {
            brain.carry(dx: d.dx, dy: d.dy)
            followUntil = lastRefresh.addingTimeInterval(1)
        }
        lastWindows = ws
        let screens = NSScreen.screens.map { ScreenInfo(frame: $0.frame, visible: $0.visibleFrame) }   // Dock 可能换屏, 每次重读
        platforms = buildPlatforms(windows: ws, ownPID: ownPID, screens: screens,
                                   catHeight: catH, minWidth: max(100, catW * 1.2))
    }

    // MARK: 时钟

    func schedule(slow: Bool) {
        timer?.invalidate()
        self.slow = slow
        let iv = slow ? 0.5 : 0.125            // 睡觉 2 fps, 其余 8 fps
        let t = Timer.scheduledTimer(withTimeInterval: iv, repeats: true) { [weak self] _ in self?.step() }
        t.tolerance = iv * 0.2
        timer = t
        lastTick = Date()
    }

    func step() {
        let now = Date()
        if now.timeIntervalSince(lastRefresh) >= (now < followUntil ? 0.1 : 0.5) { refreshPlatforms() }
        let dt = now.timeIntervalSince(lastTick)
        lastTick = now
        brain.tick(dt: dt, platforms: platforms)
        render()
        if trace, now.timeIntervalSince(lastTrace) >= 0.25 { printTrace(now) }
        let wantSlow = brain.isSleeping && !brain.isPetted && now >= followUntil
        if wantSlow != slow { schedule(slow: wantSlow) }
    }

    func printTrace(_ now: Date) {
        lastTrace = now
        let shown = overlays.enumerated().filter { !$0.element.catLayer.isHidden }.map(\.offset)
        let floors = platforms.filter { $0.windowID == nil }.map { Int($0.top) }
        print(String(format: "%.2f x=%.0f y=%.0f %@ on=%@ overlays=%@ floors=%@ platforms=%d", now.timeIntervalSince1970,
                     brain.x, brain.y, brain.frame(),
                     supportAt(platforms, x: brain.x, y: brain.y)?.windowID.map(String.init) ?? "-",
                     "\(shown)", "\(floors)", platforms.count))
    }

    /// 点一下或按着蹭都算摸: 停手后 PetTuning.release 秒自己结束.
    func pat() {
        brain.stroke()
        step()
        if slow { schedule(slow: false) }
    }

    // MARK: 渲染

    /// 拎着且有拎起帧时该画的帧. 其余情况 nil, 照常画 brain.frame().
    var liftLook: (key: String, rows: [String])? {
        guard let sheet = scruff, let p = brain.liftPose(stages: sheet.stageCount) else { return nil }
        return ("scruff/\(p.stage)/\(p.big ? "big" : "small")/\(p.index)", sheet.rows(p))
    }

    func image(_ name: String, liftRows: [String]? = nil) -> CGImage? {
        if let i = images[name] { return i }
        if liftRows == nil, let sheet = shakeSheet, let n = ShakeSheet.index(of: name), sheet.frames.indices.contains(n) {
            var buf = sheet.frames[n]
            let img: CGImage? = buf.withUnsafeMutableBytes { p in
                CGContext(data: p.baseAddress, width: sheet.width, height: sheet.height, bitsPerComponent: 8,
                          bytesPerRow: sheet.width * 4, space: CGColorSpaceCreateDeviceRGB(),
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage()
            }
            if let img { images[name] = img }
            return img
        }
        let rows = liftRows ?? skin.resolve(name)
        let w = rows[0].count
        var buf = (liftRows == nil ? nil : scruff?.rgba(rows)) ?? skin.rgba(rows)
        let img: CGImage? = buf.withUnsafeMutableBytes { p in
            CGContext(data: p.baseAddress, width: w, height: rows.count, bitsPerComponent: 8,
                      bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(),
                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage()
        }
        if let img { images[name] = img }
        return img
    }

    /// 猫在屏幕坐标里的矩形 (用于点击判定和绘制). 拎着时拎点对准鼠标, 帧高可能超过皮肤.
    var catRect: CGRect {
        if brain.isLifted {
            let m = CGPoint(x: brain.x, y: brain.y)
            if let sheet = scruff, let look = liftLook {
                return liftedRect(mouse: m, grip: sheet.grip, cols: sheet.cols, rows: look.rows.count, cell: scale)
            }
            return liftedRect(mouse: m, grip: LiftTuning.fallbackGrip, cols: skin.width, rows: skin.height, cell: scale, dir: brain.dir)
        }
        let x = (brain.x / scale).rounded() * scale
        return CGRect(x: x - catW / 2, y: brain.y.rounded() - Double(skin.footInset) * scale, width: catW, height: catH)
    }

    func render() {
        let look = liftLook
        let f = look?.key ?? brain.frame()
        var cat = catRect
        if look == nil, let sheet = shakeSheet, ShakeSheet.index(of: f) != nil {     // 拖影和括号线画在猫的格子外面
            cat = cat.insetBy(dx: -Double(sheet.pad) * scale, dy: -Double(sheet.pad) * scale)
        }
        let flip = look != nil ? 1 : brain.dir      // 拎起帧是正面的, 摆动方向也要和拖动对得上, 不镜像
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        for o in overlays {
            let visible = o.frame.intersects(cat)
            if o.catLayer.isHidden == visible { o.catLayer.isHidden = !visible }
            guard visible else { o.bubbleLayer.isHidden = true; continue }
            let r = cat.offsetBy(dx: -o.frame.minX, dy: -o.frame.minY)
            if f != o.shownFrame { o.catLayer.contents = image(f, liftRows: look?.rows); o.shownFrame = f }
            let size = CGRect(origin: .zero, size: cat.size)
            if o.catLayer.bounds != size { o.catLayer.bounds = size }
            o.catLayer.position = CGPoint(x: r.midX, y: r.midY)
            o.catLayer.setAffineTransform(CGAffineTransform(scaleX: CGFloat(flip), y: 1))
            if let text = brain.bubble {
                o.bubbleLayer.string = text
                let w = 44.0, h = 24.0
                o.bubbleLayer.frame = CGRect(x: r.midX - w / 2, y: r.maxY + 2, width: w, height: h)
                o.bubbleLayer.isHidden = false
            } else {
                o.bubbleLayer.isHidden = true
            }
        }
        CATransaction.commit()
    }

    // MARK: 鼠标: 按下在猫身上才收事件 (点一下/按着蹭是摸, 往上提是拎); 无按键蹭头靠轮询位置

    func mouse(_ e: MouseEvent) {
        let p = NSEvent.mouseLocation
        switch e {
        case .down: gesture.down(p); upMisses = 0
        case .drag: handle(gesture.moved(p), p)
        case .up: handle(gesture.up(), p)
        }
    }

    func handle(_ action: PressGesture.Action, _ p: CGPoint) {
        switch action {
        case .none: return
        case .click: pat()
        case .rub:
            if catRect.contains(p) { pat() }
        case .lift:
            tracker = ThrowTracker()
            tracker.add(x: p.x, y: p.y, at: ProcessInfo.processInfo.systemUptime)
            brain.beginLift(x: p.x, y: p.y)
            if slow { schedule(slow: false) }
            render()
        case .move:
            tracker.add(x: p.x, y: p.y, at: ProcessInfo.processInfo.systemUptime)
            brain.liftTo(x: p.x, y: p.y)
            render()
        case .drop:
            brain.liftTo(x: p.x, y: p.y)
            drop()
            render()
        }
    }

    /// 松手: 头留在原地, 换成 fall 姿势往下掉.
    func drop() {
        let r = catRect
        let head: (x: Double, top: Double)
        if let sheet = scruff {
            head = (r.minX + Double(sheet.headMidCol) * scale, r.maxY)
        } else {
            let h = headRect(frame: "fall", catRect: r, cell: scale, dir: brain.dir)
            head = (h.midX, h.maxY)
        }
        let at = dropPoint(headMidX: head.x, headTop: head.top, cell: scale, cols: skin.width, rows: skin.height,
                           footInset: skin.footInset, dir: brain.dir)
        let m = tracker.velocity(at: ProcessInfo.processInfo.systemUptime)
        let v = throwVelocity(mouseVX: m.x, mouseVY: m.y, cell: scale)
        brain.release(x: at.x, y: at.y, vx: v.vx, vy: v.vy)
    }

    func checkMouse() {
        let p = NSEvent.mouseLocation
        // 屏幕变动重建窗口时会丢拖动和 mouseUp: 这里按实际按键状态兜底, 猫不会粘在鼠标上
        if gesture.isPressed {
            if NSEvent.pressedMouseButtons & 1 != 0 {
                upMisses = 0
                handle(gesture.moved(p), p)
            } else {
                upMisses += 1                       // 第一次可能只是 mouseUp 还在队列里
                if upMisses >= 2 { handle(gesture.cancel(), p) }
            }
        }
        let cat = catRect
        let hit = gesture.isPressed || cat.contains(p)
        for o in overlays where o.window.ignoresMouseEvents == hit { o.window.ignoresMouseEvents = !hit }

        let now = Date()
        let dt = now.timeIntervalSince(lastMouseCheck)
        lastMouseCheck = now
        // 摸头只算无按键的蹭动: 按着键划过 (拎猫、从别的 App 拖东西) 都不算
        let free = NSEvent.pressedMouseButtons == 0 && !brain.isLifted
        let head = headRect(frame: brain.frame(), catRect: cat, cell: scale, dir: brain.dir)
        if rubSensor.sample(p, inHead: free && head.contains(p), dt: dt) { brain.stroke() }
    }
}

setvbuf(stdout, nil, _IOLBF, 0)
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let controller = Controller()
for sig in [SIGINT, SIGTERM] {
    signal(sig, SIG_IGN)
    let src = DispatchSource.makeSignalSource(signal: sig, queue: .main)
    src.setEventHandler { NSApp.terminate(nil) }
    src.resume()
    signalSources.append(src)
}
if let n = runSeconds {
    Timer.scheduledTimer(withTimeInterval: n, repeats: false) { _ in NSApp.terminate(nil) }
}
app.run()
