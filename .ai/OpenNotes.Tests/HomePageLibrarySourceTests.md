# OpenNotes.Tests/HomePageLibrarySourceTests.cs

> Last updated: 2026-09-05 | Protection: STANDARD

## Purpose

Source contract that Home selection Move uses on-screen folders (`IsChoosingMoveTarget`) instead of a bottom `PlacementMode.Top` menu, that Delete files goes through `TrySendToRecycleBin`, and that library tile drags do not start OLE `FileDrop` with `DragDropEffects.Move`.
