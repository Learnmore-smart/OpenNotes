(function () {
  "use strict";

  const surface = document.querySelector("[data-demo-surface]");
  const canvas = document.querySelector("[data-demo-canvas]");
  const status = document.querySelector("[data-demo-status]");
  const textBox = document.querySelector("[data-demo-text]");
  const dragGrip = document.querySelector("[data-demo-drag]");
  const undoButton = document.querySelector("[data-demo-undo]");
  const clearButton = document.querySelector("[data-demo-clear]");
  const resizeHandles = [...document.querySelectorAll("[data-demo-resize]")];
  const resizeDirections = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];

  if (!surface || !canvas || !status || !textBox || !dragGrip || !undoButton || !clearButton || resizeHandles.length !== resizeDirections.length) {
    return;
  }

  const context = canvas.getContext("2d");
  const seededMarks = [
    {
      kind: "highlighter",
      color: "rgba(241, 212, 109, 0.52)",
      width: 19,
      points: [[0.21, 0.25], [0.3, 0.24], [0.39, 0.25], [0.48, 0.24], [0.56, 0.25]]
    },
    {
      kind: "pen",
      color: "#2f6ec4",
      width: 3,
      points: [[0.19, 0.25], [0.24, 0.29], [0.29, 0.23], [0.34, 0.28], [0.4, 0.22], [0.45, 0.27], [0.52, 0.23], [0.58, 0.25]]
    },
    {
      kind: "pen",
      color: "#dd5a4f",
      width: 2.6,
      points: [[0.22, 0.68], [0.27, 0.63], [0.33, 0.7], [0.39, 0.62], [0.45, 0.67], [0.52, 0.61]]
    }
  ];

  let marks = seededMarks.map((mark) => ({ ...mark, points: mark.points.map((point) => [...point]) }));
  const undoStack = [];
  const maxUndoDepth = 40;
  let activeTool = "pen";
  let activeStroke = null;
  let eraserSnapshot = null;
  let surfaceSize = { width: 1, height: 1 };
  let resizeState = null;
  let dragState = null;

  function localized(key, fallback) {
    const locale = document.documentElement.lang || "en";
    return window.OpenNotesI18n?.copy?.[locale]?.[key] || fallback;
  }

  function resizeCanvas() {
    const bounds = surface.getBoundingClientRect();
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    surfaceSize = { width: bounds.width, height: bounds.height };
    canvas.width = Math.max(1, Math.round(bounds.width * ratio));
    canvas.height = Math.max(1, Math.round(bounds.height * ratio));
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    redraw();
    syncResizeHandles();
  }

  function toCanvasPoint(event) {
    const bounds = canvas.getBoundingClientRect();
    return {
      x: Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width)),
      y: Math.max(0, Math.min(1, (event.clientY - bounds.top) / bounds.height))
    };
  }

  function pointDistance(first, second) {
    const dx = (first[0] - second[0]) * surfaceSize.width;
    const dy = (first[1] - second[1]) * surfaceSize.height;
    return Math.sqrt(dx * dx + dy * dy);
  }

  function drawMark(mark) {
    if (mark.points.length < 2) {
      return;
    }

    context.save();
    context.globalCompositeOperation = "source-over";
    context.strokeStyle = mark.color;
    context.lineWidth = mark.width;
    context.lineCap = "round";
    context.lineJoin = "round";
    context.beginPath();
    mark.points.forEach((point, index) => {
      const x = point[0] * surfaceSize.width;
      const y = point[1] * surfaceSize.height;
      if (index === 0) {
        context.moveTo(x, y);
      } else {
        context.lineTo(x, y);
      }
    });
    context.stroke();
    context.restore();
  }

  function redraw() {
    context.clearRect(0, 0, surfaceSize.width, surfaceSize.height);
    marks.forEach(drawMark);
    if (activeStroke) {
      drawMark(activeStroke);
    }
  }

  function cloneMarks(source) {
    return source.map((mark) => ({
      ...mark,
      points: mark.points.map((point) => [...point])
    }));
  }

  function updateUndoControls() {
    const canUndo = undoStack.length > 0;
    undoButton.disabled = !canUndo;
    undoButton.setAttribute("aria-disabled", String(!canUndo));
    clearButton.disabled = marks.length === 0 && !activeStroke;
  }

  function rememberMarks(snapshot) {
    undoStack.push(cloneMarks(snapshot));
    if (undoStack.length > maxUndoDepth) {
      undoStack.shift();
    }
    updateUndoControls();
  }

  function updateStatus(message) {
    status.textContent = message;
  }

  function selectTool(tool) {
    activeTool = tool;
    canvas.dataset.tool = tool;
    document.querySelectorAll("[data-demo-tool]").forEach((button) => {
      const selected = button.dataset.demoTool === tool;
      button.classList.toggle("is-selected", selected);
      button.setAttribute("aria-pressed", String(selected));
    });

    const messages = {
      pen: localized("demo.ready", "Pen ready — draw anywhere on the page"),
      highlighter: localized("demo.highlighterReady", "Highlighter ready — pull a thought into focus"),
      eraser: localized("demo.eraserReady", "Eraser ready — sweep over a mark to remove it")
    };
    updateStatus(messages[tool]);
  }

  function eraseAt(point) {
    const radius = Math.max(16, surfaceSize.width * 0.025);
    const nextMarks = marks.filter((mark) => !mark.points.some((candidate) => pointDistance(candidate, [point.x, point.y]) < radius));
    const changed = nextMarks.length !== marks.length;
    if (changed) {
      marks = nextMarks;
      redraw();
      updateUndoControls();
    }
    return changed;
  }

  function beginDrawing(event) {
    if (event.button !== undefined && event.button !== 0 && event.pointerType !== "pen") {
      return;
    }

    event.preventDefault();
    canvas.setPointerCapture(event.pointerId);
    const point = toCanvasPoint(event);

    if (activeTool === "eraser") {
      eraserSnapshot = cloneMarks(marks);
      eraseAt(point);
      return;
    }

    activeStroke = {
      kind: activeTool,
      color: activeTool === "highlighter" ? "rgba(241, 212, 109, 0.55)" : "#2f6ec4",
      width: activeTool === "highlighter" ? 19 : 3,
      points: [[point.x, point.y]]
    };
    redraw();
  }

  function continueDrawing(event) {
    if (!canvas.hasPointerCapture(event.pointerId)) {
      return;
    }

    event.preventDefault();
    const point = toCanvasPoint(event);
    if (activeTool === "eraser") {
      eraseAt(point);
      return;
    }

    if (activeStroke) {
      activeStroke.points.push([point.x, point.y]);
      redraw();
    }
  }

  function endDrawing(event) {
    if (canvas.hasPointerCapture(event.pointerId)) {
      canvas.releasePointerCapture(event.pointerId);
    }

    if (eraserSnapshot && marks.length < eraserSnapshot.length) {
      rememberMarks(eraserSnapshot);
    }
    eraserSnapshot = null;

    if (activeStroke && activeStroke.points.length > 1) {
      rememberMarks(marks);
      marks.push(activeStroke);
    }
    activeStroke = null;
    redraw();
    updateUndoControls();
  }

  function clearMarks() {
    if (marks.length === 0) {
      updateStatus(localized("demo.undoEmpty", "Nothing to undo yet"));
      return;
    }

    rememberMarks(marks);
    marks = [];
    activeStroke = null;
    redraw();
    updateStatus(localized("demo.cleared", "Page cleared — choose a tool and draw again"));
    updateUndoControls();
  }

  function undoMarks() {
    if (undoStack.length === 0) {
      updateStatus(localized("demo.undoEmpty", "Nothing to undo yet"));
      updateUndoControls();
      return;
    }

    marks = undoStack.pop();
    activeStroke = null;
    eraserSnapshot = null;
    redraw();
    updateUndoControls();
    updateStatus(localized("demo.undone", "Last mark undone — keep drawing when you're ready"));
  }

  function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
  }

  function getTextBoxLimits() {
    const padding = 14;
    return {
      minWidth: 126,
      minHeight: 54,
      padding,
      maxLeft: Math.max(padding, surface.clientWidth - padding - 126),
      maxTop: Math.max(padding, surface.clientHeight - padding - 54),
      maxRight: Math.max(126 + padding, surface.clientWidth - padding),
      maxBottom: Math.max(54 + padding, surface.clientHeight - padding)
    };
  }

  function getTextBoxRect() {
    return {
      left: textBox.offsetLeft,
      top: textBox.offsetTop,
      width: textBox.offsetWidth,
      height: textBox.offsetHeight
    };
  }

  function setTextBoxRect(rect) {
    const limits = getTextBoxLimits();
    const left = clamp(rect.left, limits.padding, limits.maxLeft);
    const top = clamp(rect.top, limits.padding, limits.maxTop);
    const right = clamp(rect.left + rect.width, left + limits.minWidth, limits.maxRight);
    const bottom = clamp(rect.top + rect.height, top + limits.minHeight, limits.maxBottom);
    const width = clamp(right - left, limits.minWidth, limits.maxRight - left);
    const height = clamp(bottom - top, limits.minHeight, limits.maxBottom - top);

    textBox.style.left = `${left}px`;
    textBox.style.top = `${top}px`;
    textBox.style.width = `${width}px`;
    textBox.style.height = `${height}px`;
    syncResizeHandles();
  }

  function clampTextPosition(left, top, width = textBox.offsetWidth, height = textBox.offsetHeight) {
    const { padding } = getTextBoxLimits();
    return {
      left: clamp(left, padding, Math.max(padding, surface.clientWidth - padding - width)),
      top: clamp(top, padding, Math.max(padding, surface.clientHeight - padding - height))
    };
  }

  function setTextBoxPosition(left, top) {
    const position = clampTextPosition(left, top);
    textBox.style.left = `${position.left}px`;
    textBox.style.top = `${position.top}px`;
    syncResizeHandles();
  }

  function syncResizeHandles() {
    const left = textBox.offsetLeft;
    const top = textBox.offsetTop;
    const right = left + textBox.offsetWidth;
    const bottom = top + textBox.offsetHeight;
    const middleX = left + textBox.offsetWidth / 2;
    const middleY = top + textBox.offsetHeight / 2;
    const positions = {
      nw: [left, top],
      n: [middleX, top],
      ne: [right, top],
      e: [right, middleY],
      se: [right, bottom],
      s: [middleX, bottom],
      sw: [left, bottom],
      w: [left, middleY]
    };

    resizeHandles.forEach((handle) => {
      const position = positions[handle.dataset.demoResize];
      if (!position) {
        return;
      }
      handle.style.left = `${position[0]}px`;
      handle.style.top = `${position[1]}px`;
    });

    dragGrip.style.left = `${right - 14}px`;
    dragGrip.style.top = `${top}px`;
  }

  function resizeRectangle(start, direction, deltaX, deltaY) {
    const next = { ...start };
    if (direction.includes("w")) {
      next.left += deltaX;
      next.width -= deltaX;
    }
    if (direction.includes("e")) {
      next.width += deltaX;
    }
    if (direction.includes("n")) {
      next.top += deltaY;
      next.height -= deltaY;
    }
    if (direction.includes("s")) {
      next.height += deltaY;
    }
    return next;
  }

  function beginResize(event) {
    event.preventDefault();
    event.stopPropagation();
    const handle = event.currentTarget;
    const direction = handle.dataset.demoResize;
    handle.setPointerCapture(event.pointerId);
    resizeState = {
      handle,
      pointerId: event.pointerId,
      direction,
      rect: getTextBoxRect(),
      x: event.clientX,
      y: event.clientY
    };
    updateStatus(localized("demo.textResized", "Text note selected — drag the blue handle to resize"));
  }

  function continueResize(event) {
    if (!resizeState || event.pointerId !== resizeState.pointerId) {
      return;
    }

    setTextBoxRect(resizeRectangle(
      resizeState.rect,
      resizeState.direction,
      event.clientX - resizeState.x,
      event.clientY - resizeState.y
    ));
  }

  function endResize(event) {
    if (resizeState && resizeState.handle.hasPointerCapture(event.pointerId)) {
      resizeState.handle.releasePointerCapture(event.pointerId);
    }
    resizeState = null;
  }

  function keyboardResize(event) {
    const direction = event.currentTarget.dataset.demoResize;
    const horizontal = event.key === "ArrowLeft" || event.key === "ArrowRight";
    const vertical = event.key === "ArrowUp" || event.key === "ArrowDown";
    if (!horizontal && !vertical) {
      return;
    }

    event.preventDefault();
    const step = event.shiftKey ? 24 : 8;
    let deltaX = 0;
    let deltaY = 0;
    if (horizontal && (direction.includes("w") || direction.includes("e"))) {
      deltaX = event.key === "ArrowLeft" ? -step : step;
    } else if (vertical && (direction.includes("n") || direction.includes("s"))) {
      deltaY = event.key === "ArrowUp" ? -step : step;
    }

    if (deltaX === 0 && deltaY === 0) {
      return;
    }

    setTextBoxRect(resizeRectangle(getTextBoxRect(), direction, deltaX, deltaY));
    updateStatus(localized("demo.textResizeKeyboard", "Text note resized — use arrow keys to adjust width and height"));
  }

  function beginTextDrag(event) {
    if (event.button !== undefined && event.button !== 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    dragGrip.setPointerCapture(event.pointerId);
    const rect = getTextBoxRect();
    dragState = {
      pointerId: event.pointerId,
      left: rect.left,
      top: rect.top,
      x: event.clientX,
      y: event.clientY
    };
    surface.classList.add("is-dragging-text");
    updateStatus(localized("demo.dragging", "Text note moving — release it where the thought belongs"));
  }

  function continueTextDrag(event) {
    if (!dragState || event.pointerId !== dragState.pointerId || !dragGrip.hasPointerCapture(event.pointerId)) {
      return;
    }

    event.preventDefault();
    setTextBoxPosition(
      dragState.left + event.clientX - dragState.x,
      dragState.top + event.clientY - dragState.y
    );
  }

  function endTextDrag(event) {
    if (!dragState || event.pointerId !== dragState.pointerId) {
      return;
    }

    if (dragGrip.hasPointerCapture(event.pointerId)) {
      dragGrip.releasePointerCapture(event.pointerId);
    }
    dragState = null;
    surface.classList.remove("is-dragging-text");
    updateStatus(localized("demo.dragged", "Text note moved — type here or keep shaping the note"));
  }

  function keyboardTextDrag(event) {
    const horizontal = event.key === "ArrowLeft" || event.key === "ArrowRight";
    const vertical = event.key === "ArrowUp" || event.key === "ArrowDown";
    if (!horizontal && !vertical) {
      return;
    }

    event.preventDefault();
    const step = event.shiftKey ? 24 : 8;
    const deltaX = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const deltaY = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    setTextBoxPosition(textBox.offsetLeft + deltaX, textBox.offsetTop + deltaY);
    updateStatus(localized("demo.dragged", "Text note moved — type here or keep shaping the note"));
  }

  document.querySelectorAll("[data-demo-tool]").forEach((button) => {
    button.addEventListener("click", () => selectTool(button.dataset.demoTool));
  });
  undoButton.addEventListener("click", undoMarks);
  clearButton.addEventListener("click", clearMarks);

  canvas.addEventListener("pointerdown", beginDrawing);
  canvas.addEventListener("pointermove", continueDrawing);
  canvas.addEventListener("pointerup", endDrawing);
  canvas.addEventListener("pointercancel", endDrawing);
  dragGrip.addEventListener("pointerdown", beginTextDrag);
  dragGrip.addEventListener("pointermove", continueTextDrag);
  dragGrip.addEventListener("pointerup", endTextDrag);
  dragGrip.addEventListener("pointercancel", endTextDrag);
  dragGrip.addEventListener("keydown", keyboardTextDrag);
  resizeHandles.forEach((handle) => {
    handle.addEventListener("pointerdown", beginResize);
    handle.addEventListener("pointermove", continueResize);
    handle.addEventListener("pointerup", endResize);
    handle.addEventListener("pointercancel", endResize);
    handle.addEventListener("keydown", keyboardResize);
  });
  textBox.addEventListener("focus", () => updateStatus(localized("demo.textSelected", "Text note selected — type, then resize from the blue handles")));

  document.addEventListener("opennotes:localechange", () => {
    selectTool(activeTool);
  });

  function loadOptionalArtwork() {
    document.querySelectorAll("[data-art-slot]").forEach((slot) => {
      const filename = slot.dataset.artwork;
      const visual = slot.querySelector(".art-slot-visual");
      const placeholder = slot.querySelector(".art-slot-placeholder");
      const statusLabel = slot.querySelector("[data-art-status]");
      if (!filename || !visual || !placeholder) {
        return;
      }

      const updateArtworkStatus = () => {
        if (!statusLabel) {
          return;
        }

        if (slot.dataset.artworkState === "loaded") {
          statusLabel.textContent = localized("artwork.loaded", "image loaded");
        } else if (slot.dataset.artworkState === "missing") {
          statusLabel.textContent = localized("artwork.placeholder", "placeholder — add the file to show it");
        }
      };

      if (slot.dataset.artworkState === "loaded" || slot.dataset.artworkState === "missing") {
        updateArtworkStatus();
        return;
      }

      if (slot.dataset.artworkState === "pending") {
        return;
      }

      slot.dataset.artworkState = "pending";
      const artworkUrl = `assets/placeholders/${encodeURIComponent(filename)}`;
      fetch(artworkUrl, { cache: "no-store" })
        .then((response) => {
          if (!response.ok) {
            throw new Error(`Optional artwork unavailable: ${response.status}`);
          }
          return response.blob();
        })
        .then((blob) => {
          visual.style.backgroundImage = `url("${URL.createObjectURL(blob)}")`;
          visual.classList.add("has-artwork");
          placeholder.setAttribute("aria-hidden", "true");
          slot.dataset.artworkState = "loaded";
          updateArtworkStatus();
        })
        .catch(() => {
          slot.dataset.artworkState = "missing";
          updateArtworkStatus();
        });
    });
  }

  document.addEventListener("opennotes:localechange", loadOptionalArtwork);

  if ("ResizeObserver" in window) {
    new ResizeObserver(resizeCanvas).observe(surface);
  }
  window.addEventListener("resize", resizeCanvas);

  selectTool(activeTool);
  resizeCanvas();
  updateUndoControls();
  loadOptionalArtwork();
})();

(function () {
  "use strict";

  const storageKey = "opennotes-theme";
  const siteHeader = document.querySelector("[data-site-header]");

  function readStoredTheme() {
    try {
      return window.localStorage.getItem(storageKey) === "light" ? "light" : "dark";
    } catch (_error) {
      return "dark";
    }
  }

  function applyTheme(theme) {
    const activeTheme = theme === "light" ? "light" : "dark";
    document.documentElement.dataset.theme = activeTheme;
    document.querySelector("[data-demo-theme]")?.setAttribute("aria-pressed", String(activeTheme === "light"));
    document.querySelector('meta[name="theme-color"]')?.setAttribute(
      "content",
      activeTheme === "light" ? "#e6e1d8" : "#0c141d"
    );

    try {
      window.localStorage.setItem(storageKey, activeTheme);
    } catch (_error) {
      // The theme still applies when storage is unavailable.
    }
  }

  applyTheme(readStoredTheme());
  const updateHeaderMaterial = () => siteHeader?.classList.toggle("is-scrolled", window.scrollY > 12);
  updateHeaderMaterial();
  window.addEventListener("scroll", updateHeaderMaterial, { passive: true });
  document.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target.closest("[data-demo-theme]") : null;
    if (!target) {
      return;
    }

    const nextTheme = document.documentElement.dataset.theme === "light" ? "dark" : "light";
    applyTheme(nextTheme);
  });
})();
