import Testing
@testable import PerchCatCore

// 配饰: 皮肤里可选的 "accessories", 每个配饰带一套覆盖姿势. 不写就和以前完全一样.
struct AccessoryTests {
    let json = """
    {"palette":{"K":"#000000","W":"#ffffff","F":"#cc1f2f"},
     "poses":{"stand":["..",".K"],"sit":["..","KK"],"sit@happy":["W.","KK"]},
     "accessories":{"fedora":{"label":"红帽","poses":{"stand":["FF",".K"],"sit":["FF","KK"]}}}}
    """

    @Test func testParsesAccessories() throws {
        let s = try Skin.parse(json: json)
        XCTAssertEqual(s.accessoryNames, ["fedora"])
        XCTAssertEqual(s.accessories["fedora"]?.label, "红帽")
    }

    @Test func testNoAccessoriesIsUnchanged() throws {
        let s = try Skin.parse(json: ##"{"palette":{"K":"#000000"},"poses":{"stand":["K"]}}"##)
        XCTAssertTrue(s.accessories.isEmpty)
        XCTAssertEqual(s.wearing("fedora").poses, s.poses)
    }

    @Test func testWearingOverridesOnlyCoveredPoses() throws {
        let s = try Skin.parse(json: json), w = s.wearing("fedora")
        XCTAssertEqual(w.resolve("stand"), ["FF", ".K"])
        XCTAssertEqual(w.resolve("sit"), ["FF", "KK"])
        XCTAssertEqual(w.resolve("sit@happy"), ["W.", "KK"])      // 配饰没给的姿势保持原样
        XCTAssertEqual(w.wornAccessory, "fedora")
    }

    @Test func testTakingOffRestoresBase() throws {
        let s = try Skin.parse(json: json)
        let off = s.wearing("fedora").wearing(nil)
        XCTAssertEqual(off.poses, s.poses)
        XCTAssertNil(off.wornAccessory)
    }

    @Test func testUnknownAccessoryFallsBackToBase() throws {
        let s = try Skin.parse(json: json)
        XCTAssertEqual(s.wearing("crown").poses, s.poses)
        XCTAssertNil(s.wearing("crown").wornAccessory)
    }

    @Test func testAccessoryPoseSizeMustMatch() {
        let bad = json.replacingOccurrences(of: #""stand":["FF",".K"]"#, with: #""stand":["FFF",".K"]"#)
        XCTAssertThrowsError(try Skin.parse(json: bad))
    }

    @Test func testAccessoryColorsMustBeInPalette() {
        let bad = json.replacingOccurrences(of: ##","F":"#cc1f2f""##, with: "")
        XCTAssertThrowsError(try Skin.parse(json: bad))
    }
}

// 拎起和抖毛是整张图, 戴配饰时读同目录的 "名字@配饰.json", 没有就用原版
struct AccessorySheetTests {
    @Test func testSheetPathsWithAccessory() {
        let skin = "/x/perchcat/pet-skin.json"
        XCTAssertEqual(ShakeSheet.path(besideSkin: skin, accessory: "fedora"), "/x/perchcat/pet-shake@fedora.json")
        XCTAssertEqual(ScruffSheet.path(besideSkin: skin, accessory: "fedora"), "/x/perchcat/pet-scruff@fedora.json")
        XCTAssertEqual(ShakeSheet.path(besideSkin: skin, accessory: nil), ShakeSheet.path(besideSkin: skin))
        XCTAssertEqual(ScruffSheet.path(besideSkin: skin, accessory: nil), ScruffSheet.path(besideSkin: skin))
    }

    @Test func testPickSheetPrefersAccessoryVariant() {
        let have: Set<String> = ["/a/pet-shake.json", "/a/pet-shake@fedora.json"]
        XCTAssertEqual(pickSheetPath(base: "/a/pet-shake.json", variant: "/a/pet-shake@fedora.json", exists: have.contains), "/a/pet-shake@fedora.json")
        XCTAssertEqual(pickSheetPath(base: "/a/pet-shake.json", variant: "/a/pet-shake@crown.json", exists: have.contains), "/a/pet-shake.json")
        XCTAssertEqual(pickSheetPath(base: "/a/pet-shake.json", variant: nil, exists: have.contains), "/a/pet-shake.json")
    }
}
