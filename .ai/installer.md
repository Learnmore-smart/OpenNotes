# installer.iss
> Last updated: 2026-08-24（5.0.0 release metadata） | Protection: STANDARD

## Purpose

Defines the Inno Setup package for the OpenNotes Windows desktop application.

## Open Threads / Resume Context

- **Status:** ready_for_release (5.1.0)
- Version metadata and the full suite are green. Build the final self-contained installer from the release commit, install/start it with isolated data, and publish only through GitHub Release; record the final hash externally with the Release.
- **Status:** complete (5.0.1 startup-crash patch)
- The self-contained 5.0.1 installer upgraded the current per-user installation successfully; the installed executable reported file version 5.0.1.0, stayed alive for the startup smoke, and produced zero new Windows `.NET Runtime`/`Application Error` crash events. The final asset is regenerated from the committed source and its hash is recorded with the Release.
- **Status:** complete
- The installer consumes `Assets/app-icon.ico`; Inno Setup 6.7.3 produced `D:\Noah\文档\Coding\1. Open-Source\OpenNotes\installer_output\OpenNotes-Setup-5.0.0.exe` (56,085,133 bytes / 53.49 MiB; SHA-256 `4CA53AB10482267FB98172515AD8C6A607D035FE598F62FD82C0E6EB2CCA390B`). The source and publish ICO SHA-256 both equal `43E914B687B5325DE1276A69E6568E9803541862097755BA8A111E1B239AC45E`. The installer is intentionally unsigned.
- A unique temporary `/VERYSILENT /NOICONS` installation launched the installed `OpenNotes.exe` with `OPENNOTES_DATA_ROOT` isolation; its ProductVersion was `5.0.0+9d08051ff91b79be30063ea01d0fa181c7bd7685` and FileVersion `5.0.0.0`. The process was stopped after startup, uninstaller exit code was 0, and the temporary install/data directories were removed.

## Important Notes / NEVER Change

- Keep upgrade/application identifiers stable so existing installations are not stranded.
- Publish only the requested GitHub Release asset; do not deploy Pages or add a new website download page.
