# MarkLeaf macOS Dock Icon Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved S2 visual scale and B2 subtle shadow while preserving every interior design element of the current MarkLeaf icon.

**Architecture:** A Swift verifier defines measurable acceptance criteria for the committed PNG before resource generation. The bitmap edit uses the built-in image editing tool with `macos/App.png` as the edit target; the selected candidate replaces the project asset only after metric and visual checks pass. Existing scripts continue to build `.icns` from the committed PNG.

**Tech Stack:** PNG/alpha bitmap, built-in image editing, Swift/AppKit verifier, `sips`, existing ICNS resource script.

## Global Constraints

- Final canvas is 1024×1024 RGBA with transparent corners.
- Existing paper texture, ruling, rounded shape, M glyph, colors, and center of gravity remain unchanged.
- Subject scale is 96% of the current committed icon, centered.
- At 70px display size, shadow target is contact `y=1px / blur=1px / opacity=14%` plus ambient `y=5px / blur=7px / opacity=9%`.
- Strong-alpha bounds target is 81%–82% of the canvas.
- No runtime image-processing dependency is added.

---

### Task 1: Add a failing icon acceptance verifier

**Files:**
- Create: `macos/script/verify_app_icon.swift`
- Verify: `macos/App.png`

**Interfaces:**
- Consumes: one PNG path argument.
- Produces: exit 0 with dimensions and strong-alpha bounds, or exit 1 with a specific failure.

- [ ] **Step 1: Write the verifier**

Use `NSBitmapImageRep` to load the real bitmap and scan every pixel. The executable assertions are:

```swift
import AppKit
import Foundation

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data(("[icon] " + message + "\n").utf8))
    exit(1)
}

guard CommandLine.arguments.count == 2 else { fail("usage: verify_app_icon.swift <png>") }
let path = CommandLine.arguments[1]
guard let data = try? Data(contentsOf: URL(fileURLWithPath: path)),
      let rep = NSBitmapImageRep(data: data) else { fail("cannot decode PNG") }
guard rep.pixelsWide == 1024, rep.pixelsHigh == 1024 else { fail("expected 1024x1024") }
for point in [(0, 0), (1023, 0), (0, 1023), (1023, 1023)] {
    guard (rep.colorAt(x: point.0, y: point.1)?.alphaComponent ?? 1) < 0.01 else {
        fail("corner is not transparent: \(point)")
    }
}
var minX = 1024, minY = 1024, maxX = -1, maxY = -1
for y in 0..<1024 {
    for x in 0..<1024 where (rep.colorAt(x: x, y: y)?.alphaComponent ?? 0) > 64.0 / 255.0 {
        minX = min(minX, x); minY = min(minY, y)
        maxX = max(maxX, x); maxY = max(maxY, y)
    }
}
guard maxX >= minX, maxY >= minY else { fail("no strong-alpha pixels") }
let widthRatio = Double(maxX - minX + 1) / 1024.0
let heightRatio = Double(maxY - minY + 1) / 1024.0
print("[icon] bbox=(\(minX),\(minY))-(\(maxX),\(maxY)) ratio=\(widthRatio)x\(heightRatio)")
guard (0.81...0.82).contains(widthRatio), (0.81...0.82).contains(heightRatio) else {
    fail("strong-alpha bounds outside 81%-82%")
}
```

Compute `minX/minY/maxX/maxY` only from pixels with alpha greater than `64.0 / 255.0`. Print the bounding box and ratios before validating them.

- [ ] **Step 2: Run RED**

Run: `swift macos/script/verify_app_icon.swift macos/App.png`

Expected: exit 1 because current strong-alpha bounds are about 84.9%.

- [ ] **Step 3: Commit the verifier**

```bash
git add macos/script/verify_app_icon.swift
git commit -m "test(macos): define Dock icon size acceptance"
```

---

### Task 2: Produce and select the S2 + B2 bitmap

**Files:**
- Edit target: `macos/App.png`
- Scratch candidate: `work/icon-candidate.png`

**Interfaces:**
- Consumes: current `macos/App.png` as the sole edit target.
- Produces: a 1024×1024 transparent candidate preserving the interior artwork.

- [ ] **Step 1: Use the built-in image editing tool with strict invariants**

Use this exact normalized prompt:

```text
Use case: precise-object-edit
Asset type: macOS application icon master PNG
Primary request: preserve the existing MarkLeaf icon artwork exactly, scale the complete existing rounded paper icon to 96% around the canvas center, and add only a subtle macOS-style two-layer shadow outside the rounded paper edge.
Input image: Image 1 is the edit target.
Composition: 1024x1024 transparent canvas; artwork centered with unchanged optical center.
Shadow: at 70px display size, contact shadow equivalent to y=1px, blur=1px, opacity=14%; ambient shadow equivalent to y=5px, blur=7px, opacity=9%. The shadow must be barely noticeable and must not form a dark outline.
Constraints: keep the paper texture, horizontal rules, rounded-corner geometry, M glyph shape, colors, antialiasing, and all interior pixels visually unchanged; change only overall scale and exterior shadow; transparent corners; no new elements; no text changes; no watermark.
Avoid: redesign, recoloring, sharpening, added border, glow, background fill, altered M lettering, stronger shadow.
```

Save the result under `work/`; do not overwrite `macos/App.png` yet.

- [ ] **Step 2: Inspect invariants at original resolution**

Use the image viewer. Reject any candidate that changes the M glyph, paper texture, ruling, colors, or corner geometry.

- [ ] **Step 3: Verify candidate metrics**

Run: `swift macos/script/verify_app_icon.swift work/icon-candidate.png`

Expected: exit 0; both strong-alpha ratios are within 81%–82%.

- [ ] **Step 4: Replace the project asset and re-verify**

Copy the accepted candidate to `macos/App.png`, then run:

`swift macos/script/verify_app_icon.swift macos/App.png`

Expected: exit 0 with identical metrics.

- [ ] **Step 5: Commit the bitmap**

```bash
git add macos/App.png
git commit -m "fix(macos): refine Dock icon scale and shadow"
```

---

### Task 3: Verify generated sizes, bundle, and Dock appearance

**Files:**
- Verify: `macos/Resources/AppIcon.icns`
- Verify: `macos/dist/MarkLeaf.app`
- Verify: `/Applications/MarkLeaf.app`

**Interfaces:**
- Consumes: accepted `macos/App.png` and existing resource script.
- Produces: verified ICNS and installed App bundle.

- [ ] **Step 1: Generate resources and build**

Run: `macos/script/prepare_resources.sh`

Expected: `AppIcon.icns` contains 16, 32, 64, 128, 256, 512, and 1024px entries.

Run: `swift build --package-path macos`

Expected: build exits 0.

- [ ] **Step 2: Inspect small rendered sizes**

Extract 16, 32, 64, 128, and 256px previews. Confirm the M remains legible, the shadow is not clipped, and no dark outline appears.

- [ ] **Step 3: Build and install**

Run: `macos/script/build_and_run.sh --verify`

Expected: signed bundle launches and the process exists. Install the verified bundle in `/Applications`, then refresh LaunchServices/icon caches and restart Dock/Finder only as needed.

- [ ] **Step 4: Compare in the real Dock**

Place MarkLeaf beside Notes and TextEdit. Confirm S2 matches their strong visual boundary and B2 is perceived only as edge separation on light and dark Dock backgrounds.

- [ ] **Step 5: Run final verification**

Run each command separately and require exit 0:

```bash
swift macos/script/verify_app_icon.swift macos/App.png
swift test --package-path macos
swift build --package-path macos
git diff --check
git status --short
```

Expected: icon metrics pass, all tests pass, build succeeds, diff check is clean, and only intended files remain changed.
