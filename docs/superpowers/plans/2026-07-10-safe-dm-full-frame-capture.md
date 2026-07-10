# Safe DM Full-Frame Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the first full-frame scan capture from poisoning the DX hook while preserving the scanner's 2560x1369 inclusive coordinate space.

**Architecture:** `PureDM.CV` will pass only normalized in-range endpoints to the native DM capture boundary. Both the real DM file path and the injected live-capture provider will feed their decoded raw `Mat` through one dimension normalizer that accepts exact output or pads a one-pixel right/bottom shortfall with replicated border pixels; every other dimension mismatch remains an error.

**Tech Stack:** .NET 10, C# 13, x86, PureDM COM capture, Emgu CV 4.13, `CaptureRectGuardTest` console regression harness.

## Global Constraints

- Every accepted Scan click performs exactly one live capture.
- Native DM coordinates must stay within `0..windowWidth-1` and `0..windowHeight-1`.
- Public scanner and snapshot crop coordinates remain inclusive and absolute.
- Padding may add at most one right column and one bottom row and must not move any captured pixel.
- Existing user changes in both dirty repositories must remain untouched.

---

### Task 1: Safe Native Bounds and In-Memory Frame Completion

**Files:**
- Modify: `..\PureDM\CV.cs:457-748`
- Test: `Tools\CaptureRectGuardTest\Program.cs:30-320`
- Modify: `docs/superpowers/plans/2026-07-10-scan-capture-session.md:14-16`

**Interfaces:**
- Consumes: normalized inclusive capture rectangle `(x1, y1, x2, y2)` and a decoded raw `Emgu.CV.Mat`.
- Produces: `CV.NormalizeCapturedFrame(Mat captured, int expectedWidth, int expectedHeight) : Mat`, returning an owned exact-size frame.

- [ ] **Step 1: Write the failing integration regression**

Change the synthetic live-capture provider to assert that a 120x90 client is requested with safe native endpoints `(0,0,119,89)`, then return a 119x89 raw frame. Starting a capture session must still publish a 120x90 snapshot, preserve an interior pixel, and make `(119,89)` equal the replicated `(118,88)` border pixel. Remove the old assertion that expected `(120,90)` native endpoints.

- [ ] **Step 2: Run the regression and verify RED**

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj --configuration Debug --no-restore
```

Expected: FAIL because `CaptureFrame` receives `119x89` while the client is `120x90`.

- [ ] **Step 3: Implement the minimal safe capture path**

In `PureDM.CV`:

```csharp
internal static Mat NormalizeCapturedFrame(Mat captured, int expectedWidth, int expectedHeight) {
    int addRight = expectedWidth - captured.Width;
    int addBottom = expectedHeight - captured.Height;
    if ((addRight != 0 && addRight != 1) || (addBottom != 0 && addBottom != 1)) {
        throw new InvalidOperationException(
            $"Captured frame size {captured.Width}x{captured.Height} cannot be normalized to {expectedWidth}x{expectedHeight}.");
    }
    var normalized = new Mat();
    CvInvoke.CopyMakeBorder(
        captured, normalized,
        0, addBottom, 0, addRight,
        BorderType.Replicate,
        new MCvScalar());
    return normalized;
}
```

Delete `ConvertInclusiveToDmCaptureRectangle`. Call the real DM API and the injected provider with the already-normalized in-range `(x1,y1,x2,y2)` values. Route both raw results through `NormalizeCapturedFrame` before returning. Log both actual and expected dimensions when normalization rejects a frame.

- [ ] **Step 4: Run focused regression and verify GREEN**

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj --configuration Debug --no-restore
```

Expected: all capture rectangle, safe-bound, border replication, session freshness, OCR cache, and lifecycle checks PASS.

- [ ] **Step 5: Run complete x86 verification**

Run:

```powershell
dotnet build ..\PureDM\PureDM.csproj --configuration Debug --property:Platform=x86 --no-restore
dotnet build iBarter.csproj --configuration Debug --runtime win-x86 --property:Platform=x86 --no-restore
dotnet build iBarter.csproj --configuration Release --runtime win-x86 --property:Platform=x86 --no-restore
git diff --check
git -C ..\PureDM diff --check
```

Expected: all builds exit 0, no new errors, and diff checks report no whitespace errors.

- [ ] **Step 6: Review the live-DM invariant**

Audit that only `PureDM.CV.CaptureToBmpWithRetry` invokes `dm.Capture`, its endpoints are never incremented past the normalized client maximum, `captureAttempts` remains 1, and all OCR/matching paths crop the active session frame.
