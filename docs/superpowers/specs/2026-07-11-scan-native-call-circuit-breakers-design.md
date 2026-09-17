# Scan Native-Call Circuit Breakers Design

## Goal

Guarantee that one stuck native OCR, DM capture, or OpenCV operation cannot
leave the scanner button waiting indefinitely or make later scanner operations
queue behind an already stuck worker.

## Confirmed Failure Chain

The latest run logged successful completion of Remaining Diff, Color, and
Binary PureDM OCR. The next synchronous operation is
`TryRemainingTesseractOcr`, which performs Magick/Emgu preprocessing and calls
native `Tesseract.Recognize()` without a timeout. No later log was emitted.

The same unbounded local Tesseract pattern also exists in `TryRawOcr`,
`TryTemplateDiffOcr`, and `TryReadParley`. Separately, `PureDmWorker.Call`
stops waiting after eight seconds but leaves the synchronous native action
running on the only STA worker; later calls currently continue to enqueue behind
that action. Capture-session disposal and subsequent scans then wait on the same
poisoned queue.

## Local OCR Circuit Breaker

Every local Tesseract/Magick OCR invocation will run through one bounded helper:

- timeout: 2,000 ms;
- success: return the OCR value normally;
- exception: return the existing failure value;
- timeout: atomically mark local OCR poisoned and return the failure value;
- after poisoning: never start another local OCR task in that process.

At most one abandoned local native task can therefore exist. The scanner uses
the existing PureDM votes or CSV fallback after local OCR is disabled. This
trades one optional recognition vote for bounded execution.

The existing parley-specific `Task.Run` plus `Wait` is replaced by this common
helper so it cannot create additional detached Tesseract tasks after poisoning.

## PureDM Worker Circuit Breaker

The first `PureDmWorker.Call` timeout atomically marks the STA worker poisoned
and throws a dedicated timeout-derived exception. Once poisoned:

- no new actions are accepted into the queue;
- calls fail immediately with the original poison reason;
- scan code must not swallow the poison exception as an ordinary OCR miss;
- capture-session disposal is not queued behind the stuck action;
- the scan gate and UI button are released by existing outer `finally` blocks.

A poisoned worker is not restarted in-process. The original native/COM call may
still be executing, and creating another DM apartment beside it would recreate
the cross-thread/native-state corruption this worker was introduced to prevent.
The UI/log directs the operator to restart iBarter.

## Other Blocking Boundaries

- `SaveOcrDebugCapture` defaults to false so normal scans do not synchronously
  encode and write dozens of BMP files.
- The end-of-scan synchronous `Dispatcher.Invoke` log flush is removed. Log
  ordering may be eventually consistent, but scan completion never waits for UI
  text rendering.
- Fixed short retry sleeps remain; their duration is bounded.
- File and HTTP operations outside the scanner are out of scope.

## Threading Contract

DM COM creation, capture, binding, PureDM OCR, template matching, and normal
capture-session disposal remain on `PureDM-STA-Worker`. Local OCR uses a bounded
thread-pool task and is permanently disabled after its first timeout.

## Testing

Contract tests use reduced test-only timeouts to verify:

1. A slow local OCR returns within its bound, poisons local OCR, and prevents a
   second operation from starting.
2. A slow PureDM worker action poisons the worker; a subsequent call fails
   immediately instead of joining the queue.
3. Successful worker and nested-worker calls retain the same STA thread.
4. Capture-session lifecycle tests remain green.
5. Debug capture defaults off and the x86 Debug build has zero errors.

Live game verification remains necessary for the third-party native hooks, but
the tested circuit breakers bound how long the application waits for them.

## Scope

This change modifies scanner orchestration, local OCR invocation, and
`PureDmWorker` failure semantics. It does not change OCR voting weights,
template thresholds, DM binding modes, or automatically restart the application.
