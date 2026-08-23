# PageBookmarkServiceTests

## Coverage

- Verifies insert remapping shifts bookmarks at and after the inserted page without mutating input.
- Verifies a contiguous multi-page insert shifts later bookmarks by the full imported-page count
  without mutating input.
- Verifies delete remapping removes the deleted page and shifts later bookmarks backward.
- Verifies forward and backward page moves preserve labels while shifting the intervening range.
- Verifies duplicate/invalid input normalization, clone behavior, and invalid operation arguments.

## Test Contract

These tests exercise the pure `PageBookmarkService` remapping methods and do not write to the user's
AppData bookmark file. Persistence integration belongs at the successful PDF page-operation call sites
in `Pages/EditorPage.xaml.cs`; the external-import snapshot wiring is intentionally covered by the
same pure remap contract rather than a WPF UI test.
