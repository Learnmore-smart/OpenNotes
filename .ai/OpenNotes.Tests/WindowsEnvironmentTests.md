# WindowsEnvironmentTests
> Last updated: 2026-08-21 | Protection: STANDARD

## Purpose

Regression coverage for WPF startup compatibility when a host omits the `WINDIR` process variable.

## What It Does

The test temporarily removes `WINDIR`, supplies the real Windows root, invokes the internal compatibility helper, and verifies that the alias is restored. The original process environment is restored in a `finally` block.

## Dependencies

- **Internal:** `Services/WindowsEnvironment.cs` — behavior under test.
- **Framework:** NUnit and `System.Environment`.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** Preserve a focused regression test without instantiating a WPF `Window` in the test host.
- **Next steps:** None.

## Agent Decisions / Thoughts

- **2026-08-21:** Test the process-local normalization directly because the previous minimal WPF window test was blocked by the same host-level initialization failure it was intended to diagnose.

## Important Notes / NEVER Change

- Keep the test non-parallel because it temporarily changes process environment variables.
- Always restore both variables in `finally`.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-21 | Added regression coverage for missing `WINDIR`. | Codex |
