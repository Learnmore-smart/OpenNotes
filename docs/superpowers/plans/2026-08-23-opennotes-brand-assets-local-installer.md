# OpenNotes Brand Assets and Local Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the visible OpenNotes desktop/web branding with the user-provided logo bundle, produce a verified local Windows installer EXE, and commit the complete current worktree without publishing or pushing.

**Architecture:** Keep all legacy compatibility identities unchanged. Treat `public/logo/OpenNotes-logo.png` and `public/logo/OpenNotes_favicon/` as canonical source artwork, copy the optimized ICO to the WPF/installer icon resource, copy the web favicon bundle to `website/assets/`, and reference the new mark from the static site and README. Extend the existing dependency-free website checker before copying assets.

**Tech Stack:** .NET 8 WPF, Inno Setup, PowerShell, HTML/CSS, Git.

---

### Task 1: Lock the brand-asset contract

**Files:**
- Modify: `tools/check-website.ps1`
- Modify: `.ai/tools/check-website.md`

- [x] Require the new SVG/ICO/Apple Touch/manifest files, the new header mark reference, and manifest/icon link tags.
- [x] Run the checker and confirm it fails because the new assets/references are absent.

### Task 2: Integrate the canonical artwork

**Files:**
- Replace: `Assets/app-icon.ico`
- Create: `website/assets/favicon.ico`
- Replace: `website/assets/favicon.svg`
- Create: `website/assets/apple-touch-icon.png`
- Create: `website/assets/favicon-96x96.png`
- Create: `website/assets/web-app-manifest-192x192.png`
- Create: `website/assets/web-app-manifest-512x512.png`
- Create: `website/assets/site.webmanifest`
- Create: `website/assets/opennotes-logo.png`
- Modify: `website/index.html`
- Modify: `website/404.html`
- Modify: `website/theme.css`
- Modify: `README.md`

- [x] Copy binary artwork byte-for-byte from the canonical user-provided bundle.
- [x] Replace the hand-authored header glyph with the supplied optimized 96px PNG mark and add complete SVG/ICO/favicon metadata.
- [x] Add the supplied full logo to the README while preserving project copy and relative paths.
- [x] Rerun website verification and browser syntax checks.

### Task 3: Verify and build the local installer

**Files:**
- Verify: `OpenNotes.csproj`
- Verify: `installer.iss`
- Verify: `build.ps1`

- [x] Confirm the WPF application and Inno installer both consume `Assets/app-icon.ico`.
- [x] Run the full solution build, 100-test suite, i18n verifier, website verifier, and `git diff --check`.
- [x] Run the local Release publish and Inno compiler path without invoking any GitHub workflow or remote API.
- [x] Inspect the resulting EXE metadata, size, hash, and embedded icon resource; complete a temporary silent install/uninstall smoke test.

### Task 4: Documentation and local commit

**Files:**
- Modify: `.ai/PROJECT_CONTEXT.md`
- Modify relevant `.ai/` mirrors.

- [x] Record final asset provenance, installer path/hash, and verification evidence.
- [x] Review staged files for credentials, logs, temporary output, and accidental remote-publish changes.
- [x] Commit the complete authorized worktree locally; do not push, publish Pages, or create a GitHub Release.
