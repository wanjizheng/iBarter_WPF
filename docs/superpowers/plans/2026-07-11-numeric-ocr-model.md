# Numeric OCR Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure digit-only remaining-count and local OCR use the compact English digit model instead of combined Chinese models.

**Architecture:** Centralize language selection by OCR type, use it from the remaining-count PureDM calls and the local numeric Tesseract factory, and retain the current voting behavior. Add non-capturing stage logs around each remaining-count PureDM mode.

**Tech Stack:** C# 13, .NET 10 WPF, Emgu Tesseract, PureDM STA worker, console contract tests.

## Global Constraints

- All PureDM OCR calls continue through `PureDmWorker.Call`.
- `OCRType.Number` selects `eng`; word OCR retains the UI language.
- Do not change remaining-count voting, parsing, or retry order.
- Preserve unrelated dirty-worktree changes.

---

### Task 1: Test and implement OCR language policy

**Files:**
- Modify: `Tools/CaptureRectGuardTest/Program.cs`
- Modify: `CFunctions.cs:2019-2068`

**Interfaces:**
- Produces: `internal static string SelectOcrLanguage(CV.OCRType ocrType)`.

- [ ] **Step 1: Write the failing reflection test**

```csharp
var selectOcrLanguage = typeof(CFunctions).GetMethod(
    "SelectOcrLanguage",
    BindingFlags.Static | BindingFlags.NonPublic);
if (selectOcrLanguage == null
    || (string?)selectOcrLanguage.Invoke(null, new object[] { CV.OCRType.Number }) != "eng") {
    Console.Error.WriteLine("Expected OCRType.Number to select the English digit model.");
    return 1;
}
```

- [ ] **Step 2: Verify RED**

Run `dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug --no-restore`.
Expected: exit 1 with `Expected OCRType.Number` because the selector does not exist.

- [ ] **Step 3: Implement the selector and local engine use**

```csharp
internal static string SelectOcrLanguage(CV.OCRType ocrType) =>
    ocrType == CV.OCRType.Number ? NumericOcrLanguage() : CurrentOcrLanguage();
```

Construct `_tessPerThread` engines with `SelectOcrLanguage(CV.OCRType.Number)`.

- [ ] **Step 4: Verify GREEN**

Run the same contract-test command. Expected: exit 0.

### Task 2: Route remaining OCR and add boundary diagnostics

**Files:**
- Modify: `CFunctions.cs:2323-2334`

**Interfaces:**
- Consumes: `SelectOcrLanguage(CV.OCRType.Number)`.
- Produces: remaining Diff/Color/Binary OCR with start/end elapsed-time logs.

- [ ] **Step 1: Change the remaining OCR language argument**

Pass `SelectOcrLanguage(CV.OCRType.Number)` instead of `CurrentOcrLanguage()`.

- [ ] **Step 2: Add start/end logs**

For each mode, log `[DIAG-rem-ocr] <mode> start` immediately before
`PureDmWorker.Call`, then log `[DIAG-rem-ocr] <mode> end <milliseconds>ms`
after it returns. On exception log the exception type and elapsed milliseconds.

- [ ] **Step 3: Run verification**

Run:

```powershell
dotnet run --project Tools\CaptureRectGuardTest\CaptureRectGuardTest.csproj -c Debug --no-restore
dotnet build iBarter.csproj -c Debug -p:Platform=x86 --no-restore
```

Expected: both exit 0; build has zero errors.

- [ ] **Step 4: Audit numeric call sites**

Run `rg -n "OCRType.Number.*CurrentOcrLanguage|CurrentOcrLanguage\(\)" CFunctions.cs` and inspect multiline calls. Expected: active numeric OCR paths use the selector or `NumericOcrLanguage`; word paths retain `CurrentOcrLanguage`.
