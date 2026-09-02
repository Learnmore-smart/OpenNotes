# installer.iss
> Last updated: 2026-08-24（5.0.0 release metadata） | Protection: STANDARD

## Purpose

Defines the Inno Setup package for the OpenNotes Windows desktop application.

## Open Threads / Resume Context

- **Status:** ready_for_release (5.2.9 Edge-PDF compatibility release)
- The default installer version is `5.2.9`; the stable AppId and per-user upgrade behavior are preserved. Inno Setup is absent on this host, so the green local self-contained `OpenNotes.exe` publish/startup smoke is the local packaging gate and the tag-triggered GitHub workflow remains authoritative for `OpenNotes-Setup-5.2.9.exe` and the portable ZIP.
- **Status:** released (5.2.8 eraser stylus-crash hotfix)
- GitHub Actions run `33461186472` published both final assets from tag commit `a3d1569`. The downloaded installer and Portable hashes match GitHub metadata; the Portable executable reports 5.2.8.0, includes `x64/pdfium.dll`, and passed an isolated startup smoke with zero new crash events. Stable AppId and upgrade behavior remain unchanged.
- **Status:** ready_for_release (5.2.8 eraser stylus-crash hotfix)
- Default version is `5.2.8`; local release gates are GREEN and the tag-triggered workflow must build and publish the installer and portable ZIP from the verified release commit. The stable AppId and upgrade behavior remain unchanged.
- A workflow-equivalent self-contained `win-x64` publish succeeded locally. This host has no Inno Setup 6, so no local installer success is claimed; GitHub Actions installs Inno Setup and the remote release asset remains authoritative.

- **Status:** released (5.2.7 page-rotation drawing hotfix)
- Default installer version is `5.2.7`; GitHub Actions run `33389316845` published the final installer and portable ZIP. The stable AppId and upgrade behavior remain unchanged.
- **Status:** ready_for_release (5.2.0)
- The default version is `5.2.0`; the tag-triggered workflow will build and publish the installer and portable ZIP from the release commit.
- **Status:** ready_for_release (5.1.2)
- The default `MyAppOutputBaseFilename` concatenates the preprocessor version value, producing `OpenNotes-Setup-5.1.2.exe` instead of a literal brace expression when no workflow override is supplied.
- Version metadata and the full suite are green. Build the final self-contained installer from the release commit, install/start it with isolated data, and publish only through GitHub Release; record the final hash externally with the Release.
- **Status:** complete (5.0.1 startup-crash patch)
- The self-contained 5.0.1 installer upgraded the current per-user installation successfully; the installed executable reported file version 5.0.1.0, stayed alive for the startup smoke, and produced zero new Windows `.NET Runtime`/`Application Error` crash events. The final asset is regenerated from the committed source and its hash is recorded with the Release.
- **Status:** complete
- The installer consumes `Assets/app-icon.ico`; Inno Setup 6.7.3 produced `D:\Noah\文档\Coding\1. Open-Source\OpenNotes\installer_output\OpenNotes-Setup-5.0.0.exe` (56,085,133 bytes / 53.49 MiB; SHA-256 `4CA53AB10482267FB98172515AD8C6A607D035FE598F62FD82C0E6EB2CCA390B`). The source and publish ICO SHA-256 both equal `43E914B687B5325DE1276A69E6568E9803541862097755BA8A111E1B239AC45E`. The installer is intentionally unsigned.
- A unique temporary `/VERYSILENT /NOICONS` installation launched the installed `OpenNotes.exe` with `OPENNOTES_DATA_ROOT` isolation; its ProductVersion was `5.0.0+9d08051ff91b79be30063ea01d0fa181c7bd7685` and FileVersion `5.0.0.0`. The process was stopped after startup, uninstaller exit code was 0, and the temporary install/data directories were removed.

## Important Notes / NEVER Change

- Keep upgrade/application identifiers stable so existing installations are not stranded.
- Publish only the requested GitHub Release asset; do not deploy Pages or add a new website download page.
