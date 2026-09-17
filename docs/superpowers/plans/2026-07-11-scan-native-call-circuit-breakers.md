# Scan Native-Call Circuit Breakers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound every scanner-owned native OCR wait and stop new work from queuing behind a timed-out PureDM STA action.

**Architecture:** Add independent local-OCR and PureDM-worker circuit breakers. Local OCR abandons at most one timed-out thread and disables further local calls; PureDM marks its single STA worker poisoned on timeout and rejects every later call immediately.

**Tech Stack:** C# 13, .NET 10 WPF, `Task<int>`, `BlockingCollection<Action>`, Emgu Tesseract, PureDM COM, console contract tests.

## Global Constraints

- Preserve the one-thread DM COM affinity.
- Local OCR timeout is 2,000 ms in production.
- PureDM call timeout remains 8,000 ms in production.
- Do not alter OCR voting weights or template thresholds.
- Preserve unrelated dirty-worktree changes.

---

### Task 1: Local OCR timeout and circuit breaker

**Files:**
- Modify: `Tools/CaptureRectGuardTest/Program.cs`
- Modify: `CFunctions.cs:1980-2030`

**Interfaces:**
- Produces: `RunLocalOcrBounded(string, Func<int>, int)`, `IsLocalOcrPoisoned`, `LocalOcrPoisonReason`.

- [ ] **Step 1: Add a failing reflection/behavior contract**

Resolve the method and property by reflection. Invoke a 200 ms operation with a
50 ms timeout, assert result `-1`, elapsed below 500 ms, poison state true, then
invoke a second delegate and assert it was never entered.

- [ ] **Step 2: Verify RED**

Run `dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug --no-restore`.
Expected: exit 1 because `RunLocalOcrBounded` is absent.

- [ ] **Step 3: Implement the bounded helper**

Use `Task.Run(operation)` plus bounded `Wait`. Serialize starts under a private
lock, atomically poison on timeout, return `-1` on timeout/exception, and reject
later operations before creating a task.

- [ ] **Step 4: Verify GREEN**

Run the same command. Expected: the new contract passes and later existing tests continue.

### Task 2: PureDM worker timeout and circuit breaker

**Files:**
- Modify: `Tools/CaptureRectGuardTest/Program.cs`
- Modify: `PureDmWorker.cs`

**Interfaces:**
- Produces: `IsPoisoned`, `PoisonReason`, internal `CallTimeoutMilliseconds`, and `PureDmWorkerUnavailableException : TimeoutException`.

- [ ] **Step 1: Add a failing reflection/behavior contract**

At the end of worker tests, require the timeout property and poison property.
Set the timeout to 100 ms, invoke a 300 ms worker action, assert a timeout and
poison state, then assert a second call fails in under 100 ms without running.

- [ ] **Step 2: Verify RED**

Run the contract test. Expected: exit 1 because worker poison APIs are absent.

- [ ] **Step 3: Implement poison semantics in both Call overloads**

Reset poison only in `Start`. Before inline/queue execution reject poisoned
workers. On wait timeout atomically store the reason and throw the dedicated
exception. Use `CallTimeoutMilliseconds` for the wait.

- [ ] **Step 4: Verify GREEN**

Run the contract test. Expected: exit 0 and the second delegate remains uncalled.

### Task 3: Route scanner local OCR and propagate worker poison

**Files:**
- Modify: `CFunctions.cs`

**Interfaces:**
- Consumes: both circuit breakers.
- Produces: bounded remaining, raw, template-diff, and parley local OCR paths.

- [ ] **Step 1: Add an instance wrapper for timeout logging**

Call `RunLocalOcrBounded` with 2,000 ms and emit one
`[DIAG-local-ocr-disabled]` log when poison transitions from false to true.

- [ ] **Step 2: Replace all direct local OCR invocation sites**

Route `TryRemainingTesseractOcr`, fallback `TryRawOcr`, quantity `TryRawOcr`,
quantity `TryTemplateDiffOcr`, and parley `TryReadParley` through the wrapper.
Remove the parley-specific detached `Task.Run`/`Wait` block.

- [ ] **Step 3: Propagate PureDM poison**

Add dedicated catches that rethrow `PureDmWorkerUnavailableException` before
generic catches around tiled anchors, OCRStringOnce, Remaining modes, quantity
ROIs, icon slot 1/2, and the per-anchor scan loop.

- [ ] **Step 4: Avoid poisoned teardown wait**

In `RunInCaptureSession.finally`, queue session disposal only when the worker is
not poisoned. A poisoned process is intentionally restart-only.

### Task 4: Remove secondary unbounded scanner waits

**Files:**
- Modify: `CFunctions.cs`
- Modify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Produces: debug capture disabled by default and asynchronous log completion.

- [ ] **Step 1: Set `SaveOcrDebugCapture` default to false**

Add a reflection contract asserting false, then change the production default.

- [ ] **Step 2: Remove end-of-scan synchronous Dispatcher.Invoke**

Delete the no-op Background-priority flush. Existing `Log` calls already use
`BeginInvoke`, so this changes only ordering, not content.

- [ ] **Step 3: Verify all checks**

Run the contract executable, audit `.Wait`, `Recognize`, `PureDmWorker.Call`, and
`Dispatcher.Invoke` scanner call sites, run `git diff --check`, then build x86
Debug with zero errors.
