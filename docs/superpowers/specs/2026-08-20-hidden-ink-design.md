# Hidden Ink design

## Goal

Hidden Ink lets a learner draw over a word or phrase with an opaque, paper-coloured freehand mask. The original PDF and ordinary annotations remain untouched underneath. Clicking a mask reveals only that mask's covered content for a short period, then the mask returns automatically.

## Interaction contract

- The toolbar exposes a dedicated Hidden Ink tool next to the other drawing tools.
- A pen or mouse gesture creates one freehand mask stroke. The default mask is a solid white/paper colour and uses a readable, highlighter-like width.
- Every mask is hidden on load and after export. Clicking a mask temporarily hides its visual path for three seconds; clicking it again while revealed restarts the timer.
- Reveal state is session-only. Saving while a mask is revealed still writes the opaque mask, so reopening the document does not disclose the answer.
- Hidden masks are independent of ordinary ink: ordinary eraser/selection/shape actions must not mutate them accidentally.

## Persistence

`HiddenInkAnnotation` is a separate page annotation collection with a stable id, colour, alpha, width, reveal duration, and DIP point list. The JSON sidecar stores it directly. PDF export writes an opaque `/Ink` annotation with a `wna_hidden_` name prefix; the strip-and-rebuild loader recognises that prefix and restores it to the hidden collection instead of ordinary ink.

## Accessibility and safety

The tool tip explains the three-second reveal. The mask is a solid colour rather than a translucent effect, and the PDF writer always persists it regardless of the transient UI reveal state. No source PDF text is deleted or rewritten.
