# OpenNotes Task Documentation Synchronization 41-48 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Synchronize the V5 task records with the current checkout, add tasks 41–48, and preserve the Caelum/WindowsNotesApp compatibility boundary without changing source, website, `.ai`, or AppData files.

**Architecture:** Treat `docs/checklist.md`, `docs/spec.md`, and `docs/tasks.md` as the primary task record. Keep `.trae/specs/add-core-note-features/{checklist,spec,tasks}.md` aligned as the corresponding specification mirror, while retaining historical requirements and explicitly separating code-level completion from pending interactive or external-system validation.

**Tech Stack:** Markdown, `apply_patch`, PowerShell read-only checks, Git path/status inspection.

---

### Task 1: Establish the status contract

**Files:**
- Modify: `docs/checklist.md`
- Modify: `docs/tasks.md`
- Modify: `.trae/specs/add-core-note-features/checklist.md`
- Modify: `.trae/specs/add-core-note-features/tasks.md`

- [x] Record the shared status legend and the baseline evidence for Tasks 0–40.
- [x] Record Tasks 41–48 with the verified status split: 41/42 in progress, 43/44 code-level complete, 45/46/47 pending, and 48 pending final regression.
- [x] Keep the explicit note that Codex AppData/project records are not repaired in this task.

### Task 2: Extend the requirements

**Files:**
- Modify: `docs/spec.md`
- Modify: `.trae/specs/add-core-note-features/spec.md`

- [x] Add requirements and acceptance scenarios for full i18n, OpenNotes branding, resizable text boxes, the WPF theme system, GitHub Pages, Pages deployment, Codex project/session repair, and full regression validation.
- [x] Preserve the existing Caelum root/namespace/data and WindowsNotesApp AppX identity constraints as non-negotiable compatibility invariants.

### Task 3: Validate the documentation-only change

**Files:**
- Read: all modified Markdown files

- [x] Confirm every checklist line uses valid Markdown checkbox syntax and the primary/`.trae` task records agree on status.
- [x] Confirm the final Git diff contains changes only under `docs/**` and `.trae/specs/**`.
- [x] Report the exact changed paths and the unresolved external/interactive work without claiming completion for it.
