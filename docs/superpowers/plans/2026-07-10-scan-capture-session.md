# Scan Capture Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every Scan click acquire exactly one new game-window snapshot and run every anchor, template, and OCR operation against correctly translated client-coordinate crops of that snapshot.

**Architecture:** `PureDM.CV` owns a scoped `CaptureSession` backed by one immutable full-client `Mat`. Existing CV methods keep accepting absolute game-client coordinates; the capture layer converts inclusive `(x1,y1,x2,y2)` rectangles to snapshot-local OpenCV rectangles and existing match code adds the crop origin back to results. iBarter creates and disposes one session inside each `DoIdentifyRoutesHeavy` invocation and removes detached OCR timeout tasks.

**Tech Stack:** .NET 10 WPF, C# 13, x86, PureDM COM capture, Emgu CV 4.13, Emgu Tesseract, Magick.NET, console regression harness.

## Global Constraints

- Every Scan click that acquires successfully creates a new frame ID and performs one full-client live capture; no frame is reused by a later click.
- All public scanner coordinates remain absolute client coordinates.
- Scanner and snapshot rectangles use inclusive right/bottom coordinates, so snapshot crops use width `x2 - x1 + 1` and height `y2 - y1 + 1`. Native DM endpoints must remain within `width - 1`, `height - 1`; if the active hook returns a frame one pixel short on the right/bottom, complete it in OpenCV memory without moving existing pixels or issuing another capture.
- Template matches returned from a crop remain absolute by adding the requested crop's `x1/y1` exactly once.
- No route-recognition operation may call live DM capture while a scan capture session is active.
- Failed snapshot acquisition publishes no session and does not enter anchor/OCR processing.
- Do not change binding modes or automatically bind/unbind the game window.
- Preserve all pre-existing uncommitted changes in iBarter and PureDM. Do not commit implementation files because those files already contain user changes that cannot safely be separated into a commit.

---

### Task 1: Immutable Capture Frame and Coordinate Contract

**Files:**
- Create: `../PureDM/CaptureFrame.cs`
- Create: `../PureDM/CaptureSession.cs`
- Modify: `../PureDM/CV.cs`
- Modify: `../PureDM/PureDM.csproj`
- Test: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Produces: `CV.TryBeginCaptureSession(out CaptureSession session, out string reason) : bool`
- Produces: `CaptureSession.Id : long`, `Width : int`, `Height : int`
- Produces: `CaptureSession.CaptureBmpBytes(int x1, int y1, int x2, int y2) : byte[]`
- Produces: `CV.ActiveCaptureSessionId : long?`, `CV.LiveCaptureCount : int`
- Consumes: existing `CV.TryNormalizeCaptureRectangle(...)` and `CaptureByDMToMat(...)`

- [ ] **Step 1: Write failing coordinate and per-click session tests**

Add an internal constructor visible to `CaptureRectGuardTest` through `InternalsVisibleTo`. The test creates a deterministic `120x90` BGR frame provider, starts a session, requests inclusive crop `(10,20,19,29)`, decodes the returned BMP, and asserts `10x10` dimensions plus matching corner pixels. It requests a template located at absolute `(37,42)` through a search rectangle whose origin is non-zero and asserts the returned point is still `(37,42)`. It disposes the first session, starts a second session whose provider returns different pixels, and asserts provider call count `2`, distinct frame IDs, and second-frame pixels. It also asserts a nested session is rejected.

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj --configuration Debug
```

Expected: FAIL to compile because `CaptureSession`, the internal capture-provider constructor, and session properties do not exist.

- [ ] **Step 2: Implement `CaptureFrame` inclusive crop ownership**

Implement the core crop rule as:

```csharp
int cropWidth = x2 - x1 + 1;
int cropHeight = y2 - y1 + 1;
var roi = new Rectangle(x1, y1, cropWidth, cropHeight);
return new Mat(_frame, roi).Clone();
```

`CaptureFrame` owns the full `Mat`, validates that its dimensions equal the game client dimensions, returns owned crop clones, converts crops to 24-bit BMP bytes with `System.Drawing.Bitmap.Save`, and disposes its full frame once.

- [ ] **Step 3: Implement scoped session publication in `CV`**

Add a capture-session lock, active-session field, monotonically increasing frame ID, optional internal live-capture provider, and live-capture counter. `TryBeginCaptureSession` must acquire `(0,0,width-1,height-1)` once, validate its decoded dimensions, publish it atomically, and return the session.

`CaptureByDMToMat` first returns an active-session crop. Only when no session exists may it call `CaptureLiveByDMToMat`. `CaptureSession.Dispose()` removes only the matching active frame ID so a stale disposer cannot clear a later session.

- [ ] **Step 4: Preserve absolute match coordinates**

Keep `OpenCVMatchTemplate` and `OpenCVMatchTemplates` result translation at `match.X + _x1`, `match.Y + _y1`. Do not add any additional session offset: the session crop starts at the same absolute `_x1/_y1` requested by the existing caller.

- [ ] **Step 5: Run focused tests**

Run the console test command again.

Expected: capture crop, absolute coordinate, one acquisition per session, new frame per session, and nested-session tests PASS along with all existing checks.

---

### Task 2: Reusable PureDM OCR Engines Without Detached Work

**Files:**
- Modify: `../PureDM/CV.cs`
- Test: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Produces: serialized per-language Tesseract reuse inside `CV.ImageOCR`
- Produces: `CV.CachedOcrEngineCount : int` for diagnostics/tests
- Consumes: active `CaptureSession` crops from Task 1

- [ ] **Step 1: Write a failing OCR-engine reuse test**

Within one synthetic capture session, invoke `OCRString` twice for the same language on two valid rectangles. The test does not assert OCR text; it asserts that `CachedOcrEngineCount` changes from `0` to `1` after the first call and remains `1` after the second. Dispose `CV` and assert no disposal exception.

Run the console harness.

Expected: FAIL because `CachedOcrEngineCount` does not exist and `ImageOCR` still creates a new engine per call.

- [ ] **Step 2: Implement a serialized engine cache**

Add an OCR lock and `Dictionary<string, Tesseract>` using ordinal-ignore-case keys. Move the body of `ImageOCR` under the lock, resolve the engine with `GetOrCreateOcrEngine(language)`, set the numeric whitelist for numeric calls, and reuse the engine for `SetImage/Recognize/GetUTF8Text`. Do not dispose it per call. Dispose every cached engine and clear the dictionary from `CV.Dispose(bool)`.

- [ ] **Step 3: Run focused tests**

Run the console harness.

Expected: the cache-count test and all Task 1 tests PASS.

---

### Task 3: One New Snapshot Per iBarter Scan Click

**Files:**
- Modify: `CFunctions.cs`
- Modify: `View/BarterScanner.xaml.cs`
- Test: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Consumes: `CV.TryBeginCaptureSession(...)`
- Consumes: `CaptureSession.CaptureBmpBytes(...)`
- Produces: `DoIdentifyRoutesHeavy` session lifetime covering all anchor and row work
- Produces: diagnostic `[DIAG-capture-session] #<scan> frame=<id> liveCaptures=<delta>`

- [ ] **Step 1: Write failing scan-lifecycle tests**

Extract a small internal helper `RunInCaptureSession<T>(CV cv, Func<CaptureSession,T> body, out string reason)` so the no-game harness can assert: failed acquisition never invokes `body`; successful acquisition invokes it once; session is disposed after success and after an exception; two helper calls receive distinct IDs. The helper is production code used by `DoIdentifyRoutesHeavy`, not a test-only hook.

Run the console harness.

Expected: FAIL because `RunInCaptureSession` does not exist.

- [ ] **Step 2: Wrap the complete heavy scan in the session**

After `TryRefreshGameWindowSize`, record `LiveCaptureCount`, start a session, then execute anchor detection through final summary inside its `using` scope. Store the active session in an instance field only for the duration of that scope so custom OCR can request bytes. In `finally`, clear the field before disposing the session.

Every call to `DoIdentifyRoutesHeavy` corresponds to one accepted Scan click because `IdentifyRoutesGate` rejects overlap. Each accepted call starts its own session; no static frame survives the method.

- [ ] **Step 3: Route custom OCR bytes to the same frame**

Replace direct-DM `CFunctions.CaptureScreenBytes` behavior with the active session's `CaptureBmpBytes`. Debug-capture helpers must also read session bytes and write those bytes to their final debug file; they must not call `DM.Capture` during a scan.

- [ ] **Step 4: Remove the fake OCR timeout and redundant probe**

Delete `OcrStringWithTimeout`, `OcrCallTimeoutMs`, and its nested `Task.Run`. `OcrStringSafe` calls `App.myPureDM.CV.OCRString` synchronously, optionally sleeps, then performs the existing empty-result retry against the same frame. Remove `TryValidateGameCapture` calls from the zero-anchor branch; session acquisition is the single capture-health decision.

- [ ] **Step 5: Add capture-count diagnostics**

On successful session completion log the frame ID and `LiveCaptureCount` delta. The expected delta is exactly `1`. If the delta differs, log it as an invariant violation. On acquisition failure log the structured reason and return before `FindBarterAnchors`.

- [ ] **Step 6: Run scanner regression tests**

Run the console harness.

Expected: all existing and new checks PASS, including two sequential session IDs and coordinate assertions.

---

### Task 4: Full Verification and Diff Audit

**Files:**
- Verify: `../PureDM/CaptureFrame.cs`
- Verify: `../PureDM/CaptureSession.cs`
- Verify: `../PureDM/CV.cs`
- Verify: `CFunctions.cs`
- Verify: `View/BarterScanner.xaml.cs`
- Verify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Verifies all interfaces from Tasks 1–3

- [ ] **Step 1: Run the focused regression harness from a clean build output**

```powershell
dotnet clean Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj --configuration Debug
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj --configuration Debug
```

Expected: exit code `0`, every capture-session and existing guard message reports PASS.

- [ ] **Step 2: Build both projects**

```powershell
dotnet build ..\PureDM\PureDM.csproj --configuration Debug --property:Platform=x86
dotnet build iBarter.csproj --configuration Debug --runtime win-x86
```

Expected: both commands exit `0`; pre-existing warnings may remain but no new errors are introduced.

- [ ] **Step 3: Audit live-capture and task call sites**

```powershell
rg -n "DM\.Capture|GetScreenDataBmp|OcrStringWithTimeout|Task\.Run" CFunctions.cs ..\PureDM\CV.cs
```

Expected: no detached OCR timeout remains; scanner custom OCR does not call DM; only the PureDM live acquisition path reaches `DM.Capture`.

- [ ] **Step 4: Audit coordinate translation**

Confirm the implementation contains one inclusive crop conversion (`+ 1`) and one match-origin translation (`+ _x1`, `+ _y1`). Confirm tests exercise non-zero crop origins and right/bottom client boundaries.

- [ ] **Step 5: Review both dirty-worktree diffs without overwriting user changes**

```powershell
git diff -- CFunctions.cs View/BarterScanner.xaml.cs Tools/CaptureRectGuardTest/Program.cs
git -C ..\PureDM diff -- CaptureFrame.cs CaptureSession.cs CV.cs PureDM.csproj
```

Expected: only capture-session, OCR-lifecycle, tests, and directly necessary diagnostics are added on top of the user's existing changes.
