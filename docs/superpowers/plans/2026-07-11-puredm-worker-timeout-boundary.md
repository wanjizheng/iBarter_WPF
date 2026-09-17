# PureDM Worker Timeout Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent normal scans longer than eight seconds from timing out while keeping every PureDM/DM COM and stateful CV call on the single STA worker.

**Architecture:** Acquire and release `CaptureSession` through `PureDmWorker`, but execute the scan body on its existing background scan thread. Replace disposable per-call wait handles with `TaskCompletionSource` so a timed-out caller cannot invalidate completion state still owned by the worker.

**Tech Stack:** C# 13, .NET 10 WPF, `BlockingCollection<Action>`, PureDM COM, Emgu CV, console contract tests.

## Global Constraints

- The `dm.dmsoft` COM object is created, used, rebound, and disposed only on `PureDM-STA-Worker`.
- Stateful `CV` recognition entry points remain marshalled through that worker.
- The scanner gate remains held until session disposal finishes.
- Do not change binding modes, OCR matching rules, or item identification.
- Preserve all unrelated changes in the dirty iBarter and PureDM worktrees.

---

### Task 1: Capture-session timeout regression

**Files:**
- Modify: `Tools/CaptureRectGuardTest/Program.cs`
- Test: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Consumes: `CFunctions.RunInCaptureSession(CV, Action<CaptureSession>, out long, out int, out string)`
- Produces: executable assertions that the scan body runs outside the PureDM worker while acquisition stays on it.

- [ ] **Step 1: Write the failing test**

After the existing successful lifecycle assertion, record the calling thread and body thread:

```csharp
int lifecycleCallerThreadId = Environment.CurrentManagedThreadId;
int lifecycleBodyThreadId = -1;
bool firstLifecycleOk = CFunctions.RunInCaptureSession(
    lifecycleCv,
    session => {
        lifecycleBodyThreadId = Environment.CurrentManagedThreadId;
        lifecycleBodyRan = session.Id > 0;
    },
    out long lifecycleFrame1,
    out int lifecycleLiveCaptures1,
    out string lifecycleReason1);

if (lifecycleBodyThreadId != lifecycleCallerThreadId
    || lifecycleBodyThreadId == pureDmWorkerThreadId) {
    Console.Error.WriteLine(
        $"Expected scan body on caller thread {lifecycleCallerThreadId}, body={lifecycleBodyThreadId}, worker={pureDmWorkerThreadId}.");
    return 1;
}
```

- [ ] **Step 2: Run the focused executable and verify RED**

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug
```

Expected: exit 1 with `Expected scan body on caller thread`; current code runs the entire body on `PureDM-STA-Worker`.

- [ ] **Step 3: Commit the failing contract test**

```powershell
git add -- Tools/CaptureRectGuardTest/Program.cs
git commit -m "test: reproduce whole-scan worker timeout boundary"
```

### Task 2: Split capture-session lifecycle from scan execution

**Files:**
- Modify: `CFunctions.cs:953-980`
- Test: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Consumes: `PureDmWorker.Call<T>(Func<T>)`, `CV.TryBeginCaptureSession`, `CaptureSession.Dispose`
- Produces: unchanged `RunInCaptureSession` signature with begin/dispose on STA and body on caller.

- [ ] **Step 1: Implement the minimal lifecycle split**

Replace the whole-body worker call with worker-only begin and disposal:

```csharp
CaptureSession session = null;
string beginReason = "";
int captureCountBefore = cv.LiveCaptureCount;
bool started = PureDmWorker.Call(() =>
    cv.TryBeginCaptureSession(out session, out beginReason));

if (!started || session == null) {
    frameId = 0;
    liveCaptureDelta = cv.LiveCaptureCount - captureCountBefore;
    reason = beginReason;
    return false;
}

frameId = session.Id;
reason = "";
try {
    body(session);
    return true;
}
finally {
    PureDmWorker.Call(session.Dispose);
    liveCaptureDelta = cv.LiveCaptureCount - captureCountBefore;
}
```

Assign every `out` parameter on all paths and preserve body exceptions.

- [ ] **Step 2: Run the focused executable and verify GREEN**

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug
```

Expected: exit 0, including capture-session lifecycle and worker re-entrant contracts.

- [ ] **Step 3: Audit active scan calls**

Run:

```powershell
rg -n "App\.myPureDM\.(DM|CV)\." CFunctions.cs MainWindow.xaml.cs View --glob "*.cs"
```

Expected: every active scan `DM`/stateful `CV` call is inside `PureDmWorker.Call`; direct hits are comments or locked diagnostic properties only.

- [ ] **Step 4: Commit the lifecycle fix**

```powershell
git add -- CFunctions.cs
git commit -m "fix: keep whole scan outside PureDM worker timeout"
```

### Task 3: Make late worker completion safe

**Files:**
- Modify: `PureDmWorker.cs:116-169`
- Modify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Consumes: `BlockingCollection<Action>` worker queue.
- Produces: unchanged `Call(Action)` and `Call<T>(Func<T>)` public APIs backed by non-disposable completion tasks.

- [ ] **Step 1: Write a failing late-completion test**

Subscribe to first-chance exceptions, force a worker call to finish after its
eight-second caller timeout, and detect the current late `done.Set()` against a
disposed event:

```csharp
var lateDisposedEvent = new ManualResetEventSlim(false);
EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler = (_, e) => {
    if (e.Exception is ObjectDisposedException
        && Thread.CurrentThread.Name == "PureDM-STA-Worker") {
        lateDisposedEvent.Set();
    }
};
AppDomain.CurrentDomain.FirstChanceException += handler;
try {
    var timeoutCall = Task.Run(() => {
        try {
            PureDmWorker.Call(() => Thread.Sleep(8500));
        }
        catch (TimeoutException) {
        }
    });
    if (!timeoutCall.Wait(TimeSpan.FromSeconds(9))) {
        Console.Error.WriteLine("Expected caller timeout to return.");
        return 1;
    }
    Thread.Sleep(750);
    if (lateDisposedEvent.IsSet) {
        Console.Error.WriteLine(
            "Worker completed against a wait handle disposed by the timed-out caller.");
        return 1;
    }
}
finally {
    AppDomain.CurrentDomain.FirstChanceException -= handler;
    lateDisposedEvent.Dispose();
}
```

Also assert the timeout was observed, rather than accepting an unexpectedly
successful call:

```csharp
bool timeoutObserved = false;
// Set timeoutObserved = true in the TimeoutException catch.
if (!timeoutObserved) {
    Console.Error.WriteLine(
        "Expected the deliberately slow worker call to time out.");
    return 1;
}
```

Use the test program's existing project-root discovery rather than hardcoding a machine path.

- [ ] **Step 2: Run the focused executable and verify RED**

Run the same `dotnet run` command. Expected: exit 1 with `Worker completed against a wait handle disposed by the timed-out caller`.

- [ ] **Step 3: Replace disposable events with completion tasks**

For both overloads, enqueue the existing exception-capturing action and complete a `TaskCompletionSource<bool>` configured with `TaskCreationOptions.RunContinuationsAsynchronously` in `finally`. Wait using `completion.Task.Wait(8000)`. No completion object is disposed when the caller times out.

- [ ] **Step 4: Run the focused executable and verify GREEN**

Run the same `dotnet run` command. Expected: exit 0.

- [ ] **Step 5: Commit the completion-state fix**

```powershell
git add -- PureDmWorker.cs Tools/CaptureRectGuardTest/Program.cs
git commit -m "fix: preserve worker completion after caller timeout"
```

### Task 4: Full verification

**Files:**
- Verify: `CFunctions.cs`
- Verify: `PureDmWorker.cs`
- Verify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**
- Consumes: completed fixes from Tasks 1-3.
- Produces: build and regression evidence.

- [ ] **Step 1: Run contract tests**

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug
```

Expected: exit 0 with lifecycle and worker contract messages.

- [ ] **Step 2: Build iBarter x86 Debug**

```powershell
dotnet build iBarter.csproj -c Debug -p:Platform=x86
```

Expected: exit 0 with zero errors.

- [ ] **Step 3: Inspect only task diffs**

```powershell
git diff --check HEAD~3 -- CFunctions.cs PureDmWorker.cs Tools/CaptureRectGuardTest/Program.cs
git diff HEAD~3 -- CFunctions.cs PureDmWorker.cs Tools/CaptureRectGuardTest/Program.cs
```

Expected: no whitespace errors; changes are limited to lifecycle orchestration, completion state, and focused tests.

- [ ] **Step 4: Record live-test limitation**

Report that automated tests prove thread placement and lifetime behavior, while repeated live DX capture still requires the user to run two or more scans against the game.
