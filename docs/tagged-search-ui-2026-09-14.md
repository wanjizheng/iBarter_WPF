# TAG restore and shared search feedback — 2026-09-14

## Changes

- Validate the saved TAG workspace independently of the enabled switch during startup/load. A mismatching TAG session clears its in-memory route, cards and map; the saved file remains recoverable. A mismatch in the ordinary route cache alone does not invalidate a compatible TAG session.
- Persist the TAG switch on explicit clicks only. Loaded UI/layout state is reset from the saved setting, and save failures restore the previous switch value and report the error. Enabling after workspace load validates any retained session again.
- Feed all TAG search profiles into the existing Planner toolbar status area and toolbar cancellation action. Cargo no longer duplicates search progress when invoked by the toolbar.
- Report TAG mode, exchange count, route count, km, elapsed time, candidate count, termination reason, departure semantics and whether the supplied ordinary incumbent passed TAG replay validation. Ordinary log exchange counts no longer say "routes".
- Format both distances explicitly in km, including when an older external translation uses an unlabelled numeric format placeholder. Ordinary internal distances are world centimetres; TAG internal distances are metres.

## Interpreting the reported results

The objective remains total sailing distance first, route count second. Four routes at 160 km improve on three at 161 km; TAG at 147.10 km improves on both. These bounded searches do not prove global optimality. The earlier TAG result of 183.18 km cannot be attributed to one cause from the supplied log alone: the historical 30-exchange input is not retained in the current runtime snapshot. Accepted ordinary incumbents are replayed under actual TAG cargo, departure, port and slot constraints before competing with TAG candidates.

## Verification

- AutomaticRoutePlanningTests: 392 passed, zero failed.
- Isolated WPF build: zero errors (full application compilation emits existing warnings).
- WPF smoke: matching/mismatching restore, disabled startup invalidation, persistent on/off clicks, layout restoration, grouped cards, map completion and switching, settings, theme and localization checks passed.
- Real Planner status control: progress, km, cancellation terminal state and return to ordinary search passed; rendered in an isolated offscreen host. Status rendering intentionally does not drain unrelated Dispatcher work to ApplicationIdle.
- Test output: `Tools/TaggedTransportUiSmoke/bin/search-ui-check/`. Installed runtime and user inventory files were not overwritten. No commit, push or deployment in this change.
