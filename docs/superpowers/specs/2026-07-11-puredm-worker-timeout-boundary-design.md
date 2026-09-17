# PureDM Worker Timeout Boundary Design

## Goal

Prevent a normal scan longer than eight seconds from being reported as a hung
PureDM call, while preserving the rule that every PureDM/DM COM operation runs
on the one long-lived `PureDM-STA-Worker` thread.

## Confirmed Failure

`RunInCaptureSession` currently submits the entire scan body through
`PureDmWorker.Call`. The call has an eight-second timeout, but the observed scan
legitimately took 13.769 seconds. The caller therefore logged a fatal timeout,
released the scan gate, and displayed completion while the worker continued
processing the remaining anchors. The timeout does not cancel the queued action.

Because nested `PureDmWorker.Call` invocations execute inline when already on the
worker, wrapping the whole scan also disables useful per-operation timeout
boundaries.

## Chosen Design

`RunInCaptureSession` will marshal only session lifecycle operations:

1. Create the `CaptureSession` on `PureDM-STA-Worker`.
2. Run the scan body on the existing background scan thread.
3. Marshal all `DM`, `CV.OCRString`, `CV.FindPicture`, and other PureDM calls to
   the same STA worker exactly as today.
4. Permit only pure managed work and thread-safe reads/crops of the immutable
   captured frame on the background thread.
5. Dispose the session on the STA worker in `finally`.

The scanner gate remains held until processing and session disposal actually
finish. The UI must not display completion before that point.

## Thread-Safety Contract

- The `dm.dmsoft` COM object is created, used, rebound, and disposed only on
  `PureDM-STA-Worker`.
- Stateful `CV` recognition entry points remain marshalled through that worker.
- Session creation and disposal remain on that worker.
- Background work is limited to catalog traversal, string matching, collection
  preparation, immutable-frame crops protected by the session's existing locks,
  and diagnostic file writes.
- Direct `App.myPureDM.DM` or `App.myPureDM.CV` scan calls that bypass
  `PureDmWorker.Call` are treated as defects and included in the audit.

## Timeout and Error Handling

The eight-second timeout applies to individual queued operations, not the total
scan duration. A timed-out operation cannot be forcibly cancelled safely because
the underlying COM call is synchronous. After a genuine timeout, the worker is
considered unavailable until its current action actually exits; another scan
must not overlap it.

The wait-completion object must remain valid if a caller times out before the
queued action returns. A late worker completion must not call `Set` on a disposed
event.

Capture acquisition failure continues to return a structured reason without
starting recognition. No automatic unbind/rebind is introduced.

## Tests

Regression coverage will verify:

1. A scan body lasting longer than the worker's per-call timeout does not time
   out merely because total scan duration is long.
2. Session begin and disposal execute on the same PureDM worker thread.
3. Nested/stateful PureDM calls continue to execute on that worker thread.
4. A caller timeout does not cause a late `ObjectDisposedException` in the
   worker completion path.
5. Existing capture-session, nested worker, scanner geometry, and build checks
   remain green.

Live validation with the game is still required to prove recovery of the
third-party DX capture hook after repeated scans.

## Scope

The change is limited to worker waiting/lifecycle behavior and scanner capture
session orchestration in iBarter, plus focused tests. It does not change binding
modes, OCR matching rules, item identification, or automatically rebind the game.
