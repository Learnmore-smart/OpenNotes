# Task 28 — Handwriting-to-text spike

## Purpose

Record the Windows Ink Analysis feasibility result and the user-visible fallback for the current WPF/.NET 8 desktop build.

## Spike result (2026-08-19)

`Windows.UI.Input.Inking.Analysis.InkAnalyzer` is a WinRT API. This repository targets `net8.0-windows` with WPF/WinForms and does not reference Windows App SDK, CsWinRT projections, or a Windows Runtime component that exposes `InkAnalyzer`. Adding a direct `Windows.UI.Input.Inking.Analysis` call therefore does not compile in the current project and would add a platform/runtime dependency that is not present in the shipped application.

The spike is **not viable without a dependency decision**. The safe fallback is to expose a conversion action that reports a visible toast explaining that handwriting recognition is unavailable in this build; the original strokes remain untouched, so undo and document saving are safe.

## Open Threads

- If recognition becomes a product requirement, add and test a supported WinRT projection/package first, then implement stroke-to-`InkStroke` conversion behind an adapter.
- Do not silently delete or replace ink when recognition is unavailable.

## Completion Status

- The supported fallback is implemented in EditorPage: the selection action reports that recognition is unavailable and leaves all selected strokes unchanged.
