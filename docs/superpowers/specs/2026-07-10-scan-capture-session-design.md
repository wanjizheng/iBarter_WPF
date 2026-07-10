# Scan Capture Session Design

## Goal

Prevent a completed scan from degrading PureDM's DirectX capture hook so that a second scan can run immediately without waiting, rebinding, or restarting iBarter.

## Confirmed Failure Chain

The abort message is a downstream health report, not the source of the failure. A scan currently performs a new DM capture for every anchor tile, template match, OCR pass, and quantity-recognition pass. The logged two-row scan executes at least roughly 79 underlying captures before retry expansion.

`CFunctions.OcrStringWithTimeout` starts `PureDM.CV.OCRString` on a thread-pool task and stops waiting after five seconds. It cannot cancel the synchronous OCR operation. A timed-out operation therefore continues using PureDM after its caller has started retry and Binary passes, and may survive beyond the scan gate's release.

`PureDM.CV.ImageOCR` also constructs and initializes a new Tesseract engine for every call. Traditional-Chinese scans load both Chinese language models repeatedly in an x86 process, increasing native allocation churn while the capture burst is in progress.

False-positive tail anchors are normal input. Their missing edge and empty island text currently trigger four OCR attempts per row, adding capture pressure at the end of the scan. They expose the capture-lifetime defect but are not the root cause.

## Chosen Architecture

Each scan owns one immutable capture session:

1. Refresh the bound game-window dimensions.
2. Acquire one full client-area bitmap from DM before anchor detection.
3. Decode and validate the bitmap before publishing the session.
4. Route every `CV.FindPicture`, `CV.FindPictures`, and `CV.OCRString` capture request to a cloned crop of that immutable bitmap.
5. Route iBarter's custom quantity OCR byte requests to the same session.
6. Dispose the session after the scan completes or fails.
7. A later scan must acquire a new bitmap and must never reuse a previous scan's session.

The session is deliberately synchronous and single-owner. The existing `IdentifyRoutesGate` remains the outer exclusion boundary. Nested or overlapping capture sessions are rejected so a programming error cannot silently replace the active frame.

## PureDM Changes

PureDM will expose a scoped capture-session API on `CV`. Starting a session performs the only live `DM.Capture` required by the scan. Scanner and snapshot rectangles retain inclusive right/bottom coordinates. The native DM call must never exceed the valid client maximum (`width - 1`, `height - 1`): the active DX hook returns an image one pixel smaller on the right and bottom for those safe bounds, so PureDM expands that decoded image to the expected client size in memory by replicating only the missing right column and bottom row. While active, `CaptureByDMToMat` normalizes requested screen coordinates, checks that they fit the snapshot, and returns an owned crop clone. Callers continue disposing returned `Mat` instances exactly as they do today.

The session also exposes BMP bytes for iBarter's Magick.NET and custom Tesseract paths. Encoding occurs from the in-memory crop; it does not call DM and does not create shared temporary files.

The initial full-frame acquisition uses the existing unique temporary-path and stable-file validation contract. It performs one bounded acquisition rather than recursively retrying every recognition operation. A failed or unreadable acquisition returns a structured failure reason and leaves no active session.

Tesseract engines used by `CV.OCRString` will be cached by OCR language on the `CV` instance. Access to each cached engine is serialized because Emgu Tesseract instances are not treated as thread-safe. Cached engines are disposed when `CV` is disposed.

## iBarter Changes

`DoIdentifyRoutesHeavy` starts the capture session immediately after window-size refresh and wraps all anchor and row processing in its scope. If acquisition fails, it logs one capture failure and returns without anchor matching, OCR retry, or a second probe.

`CaptureScreenBytes` first reads from the active PureDM session. Direct DM capture remains unavailable during route recognition so a future call site cannot accidentally bypass the one-frame invariant.

`OcrStringWithTimeout` and its `Task.Run` wrapper are removed. OCR executes synchronously on the already-background scan worker. Empty text may use the existing Color-to-Binary fallback, but the fallback reads the same frame and cannot create a detached capture task.

The zero-anchor branch reports either:

- snapshot acquisition failed, before recognition began; or
- snapshot succeeded but `anchor.bmp` did not match.

It no longer performs a second live capture probe. This makes the diagnostic deterministic and prevents the health check itself from adding load.

Tail-anchor detection remains as a performance optimization. It may skip remaining rows after two consecutive failures following a success, but capture correctness does not depend on that heuristic.

## Error Handling and Lifecycle

- Session publication is atomic: consumers either see a complete decoded frame or no session.
- Every crop is independently owned and disposed by its caller.
- Session disposal clears the active frame even when anchor matching or OCR throws.
- Starting a second session while one is active fails explicitly.
- A failed initial capture never falls back to an old frame.
- DM is never automatically unbound or rebound from the scan worker.
- Recognition exceptions retain their current per-anchor isolation and scan summary logging.

## Test Design

Tests will cover the behavior without requiring a running game client:

1. A synthetic capture provider is invoked exactly once while multiple crops, picture searches, and OCR inputs are requested.
2. Crop geometry clamps client bounds correctly and preserves absolute match coordinates.
3. Session disposal releases the frame and a second session obtains a different frame.
4. Nested sessions are rejected.
5. Failed acquisition publishes no session and recognition is not invoked.
6. In-memory BMP byte crops decode to the requested dimensions.
7. The OCR execution path completes before returning and cannot leave a detached task.
8. Existing capture-rectangle, item geometry, localization, build, and scanner tests remain green.

The final verification includes the focused capture-session tests, the existing `CaptureRectGuardTest` executable, a complete iBarter build, and a review of both repositories' diffs. Live DX-hook verification still requires running the game, so the application will log the per-scan live-capture count; the expected value is exactly one for a successfully acquired scan frame.

## Scope Boundaries

This change modifies iBarter and its referenced `..\PureDM` project. It does not change binding modes, automate rebinds, alter OCR matching rules, or refactor unrelated planner/storage/UI code. Existing user changes in both dirty worktrees must be preserved.
