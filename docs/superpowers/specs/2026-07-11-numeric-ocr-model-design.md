# Numeric OCR Model Design

## Goal

Prevent the remaining-count stage from loading or running combined Chinese
Tesseract models for digit-only input, without changing its voting rules.

## Confirmed Boundary

The latest scan completed the remaining-count diagnostic crop and emitted no
later remaining result. The next operations are three `OCRType.Number` calls.
Unlike the parley and quantity paths, they pass `CurrentOcrLanguage()`, which is
`chi_sim+chi_tra` under the Traditional Chinese UI. The local digit-only
Tesseract factory also uses the UI language despite applying a numeric
whitelist.

## Design

- Introduce one testable OCR-language selector: numeric OCR returns `eng`;
  word OCR retains the current UI language.
- Route the remaining-count Diff, Color, and Binary calls through that selector.
- Construct the local digit-only Tesseract engine with the numeric selection.
- Preserve the existing three-mode plus local-Tesseract vote and all parsing.
- Add start/end diagnostics around each PureDM remaining mode. Diagnostics must
  identify the mode and elapsed milliseconds but must not add captures.

## Threading

All PureDM `OCRString` calls continue to execute through
`PureDmWorker.Call` on `PureDM-STA-Worker`. This change affects only the OCR
model selected on that thread.

## Tests

- A number request selects `eng` independently of UI language.
- Existing word OCR behavior remains selectable through the same policy.
- Existing capture-session and worker-thread contracts remain green.
- The x86 Debug application builds with zero errors.

Live verification requires scanning the game UI because a synthetic test cannot
reproduce a native Tesseract/DX-hook stall.
