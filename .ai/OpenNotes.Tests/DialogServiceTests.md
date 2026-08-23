# OpenNotes.Tests/DialogServiceTests.cs
> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Regression coverage for the runtime error/info dialog's WPF control-template construction.

## Coverage

- The close-button `ControlTemplate` contains its focus trigger before it is assigned to a live `Button`.
- The test runs STA and supplies the process-local `WINDIR` alias from `SystemRoot` when the test host omits it, matching the application's WPF startup guard.

## Open Threads / Resume Context

- **Status:** in_progress
- The template contract is added before the implementation fix so the sealed-collection failure remains reproducible during TDD.
