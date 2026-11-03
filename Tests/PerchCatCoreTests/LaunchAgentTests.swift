import Testing
@testable import PerchCatCore

struct LaunchAgentTests {
    @Test func plistRunsAtLoadWithoutKeepAlive() {
        let p = LaunchAgent.plist(executable: "/x/perchcat", skinPath: "/y/skin.json")
        #expect(p["Label"] as? String == LaunchAgent.label)
        #expect(p["RunAtLoad"] as? Bool == true)
        #expect(p["KeepAlive"] == nil)
        #expect(p["ProgramArguments"] as? [String] == ["/x/perchcat", "--skin", "/y/skin.json"])
    }

    @Test func installWritesReadablePlistAndRemoveDeletesIt() throws {
        try withTempAgent { a in
            #expect(!a.isInstalled)
            try a.install(executable: "/x/perchcat", skinPath: "/y/skin.json")
            #expect(a.isInstalled)
            let back = try readPlist(a)
            #expect(back?["RunAtLoad"] as? Bool == true)
            #expect(back?["KeepAlive"] == nil)
            try a.remove()
            #expect(!a.isInstalled)
        }
    }

    @Test func removingWhenAbsentIsFine() throws {
        try withTempAgent { a in
            try a.remove()
            #expect(!a.isInstalled)
        }
    }

    @Test func defaultLocationIsUserLaunchAgents() {
        #expect(LaunchAgent.defaultURL.path.hasSuffix("/Library/LaunchAgents/\(LaunchAgent.label).plist"))
    }
}

struct DefaultSkinTests {
    @Test func defaultSkinLivesInApplicationSupport() {
        #expect(defaultSkinPath(home: "/h") == "/h/Library/Application Support/perchcat/pet-skin.json")
    }
}

struct BuiltinSkinTests {
    @Test func builtinCatHasEveryPoseTheBrainUses() throws {
        let s = try Skin.parse(json: builtinSkinJSON)
        #expect(s.width == 24 && s.height == 24)
        let names = ["stand", "sit", "sit@happy", "half", "crouch", "land", "fall", "rise", "apex",
                     "sleep0", "sleep1", "knead0", "knead1", "puff0", "puff1", "lick0", "lick1", "lick2"]
            + (0..<8).map { "walk\($0)" }
        for n in names { #expect(s.poses[n] != nil, "缺姿势 \(n)") }
    }

    @Test func builtinLiftAndShakeSheetsParse() throws {
        let sc = try ScruffSheet.parse(builtinScruffJSON.utf8Data)
        #expect(sc.stageCount == 5)
        let heights = (0..<5).map { sc.rows(LiftPose(stage: $0, big: false, index: 0)).count }
        #expect(heights.first == 24 && heights.last == 34 && heights == heights.sorted())
        let sh = try ShakeSheet.parse(builtinShakeJSON.utf8Data)
        #expect(sh.frames.count == 2 && sh.width == 40 && sh.pad == 8)
    }

    @Test func explicitSkinWinsThenDefaultThenBuiltin() {
        #expect(chooseSkin(explicit: "/a", defaultPath: "/d", exists: { _ in true }) == "/a")
        #expect(chooseSkin(explicit: "/a", defaultPath: "/d", exists: { _ in false }) == "/a")   // 写错了要报错, 不悄悄换猫
        #expect(chooseSkin(explicit: nil, defaultPath: "/d", exists: { _ in true }) == "/d")
        #expect(chooseSkin(explicit: nil, defaultPath: "/d", exists: { _ in false }) == nil)
    }

    @Test func autostartWithBuiltinSkinPassesNoSkinArgument() {
        let p = LaunchAgent.plist(executable: "/x/perchcat", skinPath: nil)
        #expect(p["ProgramArguments"] as? [String] == ["/x/perchcat"])
    }
}
