"""
build_website_artworks.py
Generates the approved artwork assets for the OpenNotes GitHub Pages website:
1. hero-editor.webp (1600 x 1000)
2. annotation-ink.webp (1200 x 800)
3. textbox-resize.webp (1200 x 800)
4. dark-theme.webp (1200 x 800)
5. page-templates.webp (1200 x 800)
6. opennotes-mark.svg (512 x 512)
"""

import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance
import numpy as np

OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "..", "website", "assets", "placeholders")
os.makedirs(OUTPUT_DIR, exist_ok=True)

# Helper to find standard fonts
def get_font(family="segoeui", size=16, bold=False):
    win_fonts = os.environ.get("WINDIR", r"C:\Windows") + r"\Fonts"
    candidates = []
    if family == "segoeui":
        if bold:
            candidates = ["seguisb.ttf", "segoeuib.ttf", "arialbd.ttf"]
        else:
            candidates = ["segoeui.ttf", "arial.ttf"]
    elif family == "mono":
        if bold:
            candidates = ["cascadiacodeb.ttf", "consylab.ttf", "courbd.ttf"]
        else:
            candidates = ["cascadiacode.ttf", "consola.ttf", "cour.ttf"]
    elif family == "serif":
        candidates = ["timesbd.ttf" if bold else "times.ttf", "georgiab.ttf" if bold else "georgia.ttf"]
    else:
        candidates = ["segoeui.ttf", "arial.ttf"]

    for name in candidates:
        path = os.path.join(win_fonts, name)
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                pass
    return ImageFont.load_default()


def build_hero_editor():
    print("Building hero-editor.webp (1600 x 1000)...")
    # Base on public/en-demo.png (2879 x 1818)
    src_path = os.path.join(os.path.dirname(__file__), "..", "public", "en-demo.png")
    if os.path.exists(src_path):
        src = Image.open(src_path).convert("RGBA")
        # Target aspect ratio 1.6 (1600 / 1000)
        target_w, target_h = 1600, 1000
        src_w, src_h = src.size
        # Current aspect ratio = 2879 / 1818 = 1.5836
        # Crop width or height slightly to match 1.6
        crop_h = int(src_w / 1.6)
        if crop_h <= src_h:
            top = (src_h - crop_h) // 2
            cropped = src.crop((0, top, src_w, top + crop_h))
        else:
            crop_w = int(src_h * 1.6)
            left = (src_w - crop_w) // 2
            cropped = src.crop((left, 0, left + crop_w, src_h))
        
        # High-quality resize with Lanczos
        resized = cropped.resize((target_w, target_h), Image.Resampling.LANCZOS)
        # Subtle unsharp mask for crystal clear text
        sharpened = resized.filter(ImageFilter.UnsharpMask(radius=1.0, percent=115, threshold=3))
        out_path = os.path.join(OUTPUT_DIR, "hero-editor.webp")
        sharpened.convert("RGB").save(out_path, "WEBP", quality=95, method=6)
        print(f"  -> Saved {out_path} ({os.path.getsize(out_path)} bytes)")


def build_annotation_ink():
    print("Building annotation-ink.webp (1200 x 800)...")
    target_w, target_h = 1200, 800
    img = Image.new("RGBA", (target_w, target_h), "#f3f4f6")
    draw = ImageDraw.Draw(img)

    # 1. Background app chrome & paper
    # Top title bar / toolbar background
    draw.rectangle([0, 0, target_w, 110], fill="#ffffff", outline="#e5e7eb", width=1)
    
    # Left sidebar peek
    draw.rectangle([0, 110, 180, target_h], fill="#f8f9fa", outline="#e5e7eb", width=1)
    # Thumbnail card in sidebar
    draw.rounded_rectangle([18, 130, 162, 320], radius=8, fill="#ffffff", outline="#2563eb", width=2)
    # Mini page representation
    draw.rectangle([30, 145, 150, 155], fill="#e5e7eb")
    draw.rectangle([30, 165, 140, 172], fill="#e5e7eb")
    draw.rectangle([30, 180, 130, 187], fill="#e5e7eb")
    draw.line([(40, 210), (120, 230), (80, 260)], fill="#2563eb", width=2)
    draw.text((80, 328), "Page 1", fill="#1f2937", font=get_font("segoeui", 12, True))

    # Paper surface
    paper_left = 240
    paper_top = 130
    paper_right = target_w - 40
    paper_bottom = target_h + 200
    # Paper shadow
    for offset in range(12, 0, -2):
        draw.rounded_rectangle(
            [paper_left - offset, paper_top - offset//2, paper_right + offset, paper_bottom],
            radius=12,
            fill=(0, 0, 0, int(3 + offset * 1.5))
        )
    # Main paper
    draw.rounded_rectangle([paper_left, paper_top, paper_right, paper_bottom], radius=10, fill="#ffffff", outline="#d1d5db", width=1)

    # Document Header & Text
    font_h1 = get_font("segoeui", 26, True)
    font_h2 = get_font("segoeui", 18, True)
    font_body = get_font("segoeui", 15, False)
    font_sub = get_font("segoeui", 13, False)

    draw.text((paper_left + 50, paper_top + 40), "SECTION 4: DIGITAL INK & PRESSURE DYNAMICS", fill="#1e293b", font=font_h1)
    draw.text((paper_left + 50, paper_top + 80), "4.1 Continuous Bézier Curve Fitting for Stylus Strokes", fill="#3b82f6", font=font_h2)

    body_text_lines = [
        "Windows Ink captures raw pen coordinates at up to 240Hz, generating an ultra-dense stream of input points.",
        "To achieve real-time responsiveness (<12ms), OpenNotes passes points directly to an adaptive fitting pipeline.",
        "1. Real-time quadratic smoothing reduces jitter from rapid stylus movement without sacrificing sharp corners.",
        "2. Dynamic stroke width modulation accurately reflects pen pressure and contact angle characteristics.",
        "3. Pressure sensitivity curve: W(p) = W_base · (0.2 + 0.8 · p^γ) where γ ≈ 0.75 for natural paper resistance.",
        "4. Highlighter strokes are blended in pre-multiplied Alpha space to preserve background legibility."
    ]

    curr_y = paper_top + 125
    for line in body_text_lines:
        draw.text((paper_left + 50, curr_y), line, fill="#334155", font=font_body)
        curr_y += 36

    # 2. Rich Annotations (Highlighter, Pen, Arrows, Margin notes)
    # Yellow highlighter under line 1
    hl_overlay = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    hl_draw = ImageDraw.Draw(hl_overlay)
    hl_draw.rounded_rectangle([paper_left + 45, paper_top + 122, paper_left + 780, paper_top + 148], radius=6, fill=(254, 240, 138, 140))
    # Cyan highlighter under line 2
    hl_draw.rounded_rectangle([paper_left + 350, paper_top + 158, paper_left + 590, paper_top + 184], radius=6, fill=(186, 230, 253, 160))
    img = Image.alpha_composite(img, hl_overlay)
    draw = ImageDraw.Draw(img)

    # Handwritten pen notes (Blue Ink)
    # Circle formula
    pen_color = (37, 99, 235, 255) # Blue
    # Draw smooth handwritten ellipse around formula
    points = []
    cx, cy, rx, ry = paper_left + 370, paper_top + 270, 280, 24
    for a in np.linspace(0, 2*math.pi + 0.3, 80):
        # Add natural wobble
        wobble_x = math.sin(a * 4) * 3
        wobble_y = math.cos(a * 3) * 2
        px = cx + (rx + wobble_x) * math.cos(a)
        py = cy + (ry + wobble_y) * math.sin(a)
        points.append((px, py))
    draw.line(points, fill=pen_color, width=3, joint="curve")

    # Arrow from formula to margin note
    arrow_pts = [(paper_left + 655, paper_top + 270), (paper_left + 730, paper_top + 290), (paper_left + 770, paper_top + 340)]
    draw.line(arrow_pts, fill=pen_color, width=3, joint="curve")
    # Arrow head
    draw.polygon([(paper_left + 770, paper_top + 340), (paper_left + 755, paper_top + 328), (paper_left + 760, paper_top + 345)], fill=pen_color)

    # Margin handwriting note
    font_hw = get_font("segoeui", 17, True)
    draw.text((paper_left + 660, paper_top + 355), "★ Verified in v5.0!", fill=pen_color, font=font_hw)
    draw.text((paper_left + 660, paper_top + 382), "Low latency (<10ms)", fill="#059669", font=font_hw)
    draw.text((paper_left + 660, paper_top + 409), "Smooth Bézier curves", fill=pen_color, font=get_font("segoeui", 15, False))

    # Coral emphasis star and underline
    coral_color = (225, 29, 72, 255)
    draw.line([(paper_left + 50, paper_top + 236), (paper_left + 280, paper_top + 236)], fill=coral_color, width=3)
    draw.text((paper_left + 290, paper_top + 225), "Important", fill=coral_color, font=get_font("segoeui", 14, True))

    # 3. Top Toolbar & Floating Pen Color Palette
    # Toolbar icons
    draw.rectangle([0, 0, target_w, 64], fill="#ffffff", outline="#e2e8f0", width=1)
    tools = [
        ("Undo", False), ("Redo", False), ("|", False),
        ("Pen", True), ("Highlighter", False), ("Eraser", False), ("Shape", False),
        ("Text", False), ("Laser", False), ("Ruler", False), ("Select", False)
    ]
    tx = 30
    for name, is_active in tools:
        if name == "|":
            draw.line([(tx + 10, 16), (tx + 10, 48)], fill="#cbd5e1", width=1)
            tx += 25
            continue
        tw = 80 if name in ["Pen", "Highlighter"] else 70
        if is_active:
            draw.rounded_rectangle([tx, 12, tx + tw, 52], radius=8, fill="#eff6ff", outline="#2563eb", width=2)
            draw.text((tx + 18, 22), name, fill="#2563eb", font=get_font("segoeui", 14, True))
        else:
            draw.rounded_rectangle([tx, 12, tx + tw, 52], radius=8, fill=None)
            draw.text((tx + 16, 22), name, fill="#475569", font=get_font("segoeui", 14, False))
        tx += tw + 8

    # Floating Pen Palette popup underneath Pen button
    pal_x = 135
    pal_y = 66
    pal_w = 340
    pal_h = 100
    # Shadow
    for off in range(10, 0, -2):
        draw.rounded_rectangle([pal_x - off, pal_y - off//2, pal_x + pal_w + off, pal_y + pal_h + off], radius=14, fill=(0, 0, 0, int(4 + off * 2)))
    # Popup container
    draw.rounded_rectangle([pal_x, pal_y, pal_x + pal_w, pal_y + pal_h], radius=12, fill="#ffffff", outline="#cbd5e1", width=1)
    draw.text((pal_x + 16, pal_y + 12), "PEN COLOR & STROKE", fill="#64748b", font=get_font("segoeui", 11, True))

    # Color swatches
    swatches = [
        ("#2563eb", True),  # Active Blue
        ("#ef4444", False), # Red
        ("#10b981", False), # Emerald
        ("#f59e0b", False), # Amber
        ("#8b5cf6", False), # Violet
        ("#1e293b", False), # Slate
    ]
    sx = pal_x + 16
    for hex_c, active in swatches:
        if active:
            draw.ellipse([sx - 3, pal_y + 36, sx + 31, pal_y + 70], outline="#2563eb", width=2)
            draw.ellipse([sx, pal_y + 39, sx + 28, pal_y + 67], fill=hex_c)
        else:
            draw.ellipse([sx, pal_y + 39, sx + 28, pal_y + 67], fill=hex_c)
        sx += 38

    # Stroke thickness slider
    slider_x = pal_x + 245
    draw.line([(slider_x, pal_y + 53), (slider_x + 75, pal_y + 53)], fill="#cbd5e1", width=4)
    draw.line([(slider_x, pal_y + 53), (slider_x + 40, pal_y + 53)], fill="#2563eb", width=4)
    draw.ellipse([slider_x + 35, pal_y + 46, slider_x + 49, pal_y + 60], fill="#2563eb", outline="#ffffff", width=2)

    out_path = os.path.join(OUTPUT_DIR, "annotation-ink.webp")
    img.convert("RGB").save(out_path, "WEBP", quality=95, method=6)
    print(f"  -> Saved {out_path} ({os.path.getsize(out_path)} bytes)")


def build_textbox_resize():
    print("Building textbox-resize.webp (1200 x 800)...")
    target_w, target_h = 1200, 800
    img = Image.new("RGBA", (target_w, target_h), "#f3f4f6")
    draw = ImageDraw.Draw(img)

    # Top app chrome
    draw.rectangle([0, 0, target_w, 70], fill="#ffffff", outline="#e5e7eb", width=1)
    
    # Active text tool in toolbar
    draw.rounded_rectangle([30, 12, 110, 56], radius=8, fill="#eff6ff", outline="#2563eb", width=2)
    draw.text((50, 24), "Text", fill="#2563eb", font=get_font("segoeui", 15, True))

    # Text formatting strip on toolbar
    draw.line([(130, 18), (130, 52)], fill="#cbd5e1", width=1)
    # Font selector
    draw.rounded_rectangle([150, 16, 310, 52], radius=6, fill="#f8f9fa", outline="#cbd5e1", width=1)
    draw.text((165, 25), "Segoe UI (Regular)", fill="#1e293b", font=get_font("segoeui", 13, False))
    draw.text((285, 25), "▾", fill="#64748b", font=get_font("segoeui", 13, False))

    # Size selector
    draw.rounded_rectangle([325, 16, 385, 52], radius=6, fill="#f8f9fa", outline="#cbd5e1", width=1)
    draw.text((345, 25), "16", fill="#1e293b", font=get_font("segoeui", 13, False))
    draw.text((368, 25), "▾", fill="#64748b", font=get_font("segoeui", 13, False))

    # Bold / Italic / Color buttons
    draw.rounded_rectangle([395, 16, 430, 52], radius=6, fill="#e2e8f0", outline="#94a3b8", width=1)
    draw.text((408, 24), "B", fill="#1e293b", font=get_font("segoeui", 14, True))
    draw.rounded_rectangle([438, 16, 473, 52], radius=6, fill="#f8f9fa", outline="#cbd5e1", width=1)
    draw.text((453, 24), "I", fill="#64748b", font=get_font("segoeui", 14, True))
    # Color pill
    draw.rounded_rectangle([482, 16, 525, 52], radius=6, fill="#f8f9fa", outline="#cbd5e1", width=1)
    draw.ellipse([494, 25, 514, 45], fill="#1e40af")

    # Paper surface
    paper_x = 100
    paper_y = 100
    paper_w = target_w - 200
    paper_h = target_h + 300
    for offset in range(12, 0, -2):
        draw.rounded_rectangle([paper_x - offset, paper_y - offset//2, paper_x + paper_w + offset, paper_y + paper_h], radius=12, fill=(0, 0, 0, int(3 + offset * 1.5)))
    draw.rounded_rectangle([paper_x, paper_y, paper_x + paper_w, paper_y + paper_h], radius=10, fill="#ffffff", outline="#d1d5db", width=1)

    # Document underlying content
    font_title = get_font("segoeui", 24, True)
    font_body = get_font("segoeui", 15, False)
    draw.text((paper_x + 60, paper_y + 40), "Chapter 2: Dynamic Page Layouts and Annotations", fill="#1e293b", font=font_title)
    
    underlying_lines = [
        "Interactive documents require agile annotation layers that can adapt to varying margin widths.",
        "OpenNotes resizable text boxes provide full 8-point geometric transforms with direct font rendering.",
        "Users can click and drag handles from any edge or corner to re-shape text notes cleanly around formulas."
    ]
    uy = paper_y + 90
    for l in underlying_lines:
        draw.text((paper_x + 60, uy), l, fill="#64748b", font=font_body)
        uy += 32

    # --- Resizable Text Box in Action ---
    tb_x = paper_x + 120
    tb_y = paper_y + 230
    tb_w = 680
    tb_h = 320

    # Text box selection background & frame
    tb_overlay = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    tb_draw = ImageDraw.Draw(tb_overlay)
    # Subtle selection glow
    tb_draw.rounded_rectangle([tb_x, tb_y, tb_x + tb_w, tb_y + tb_h], radius=6, fill=(239, 246, 255, 230), outline=(37, 99, 235, 255), width=2)
    img = Image.alpha_composite(img, tb_overlay)
    draw = ImageDraw.Draw(img)

    # Text content inside text box
    tb_font_h = get_font("segoeui", 18, True)
    tb_font_p = get_font("segoeui", 16, False)
    tb_font_code = get_font("mono", 14, False)

    draw.text((tb_x + 28, tb_y + 24), "📌 Key Architecture Takeaways:", fill="#1e3a8a", font=tb_font_h)
    draw.text((tb_x + 28, tb_y + 64), "• Hardware-accelerated Direct2D / WPF rendering pipeline", fill="#1f2937", font=tb_font_p)
    draw.text((tb_x + 28, tb_y + 98), "• Sub-pixel font smoothing with Segoe UI & Cascadia Code", fill="#1f2937", font=tb_font_p)
    draw.text((tb_x + 28, tb_y + 132), "• 8-Direction bounding handle transform with constraint preserving", fill="#1f2937", font=tb_font_p)
    
    # Code box inside note
    draw.rounded_rectangle([tb_x + 28, tb_y + 175, tb_x + tb_w - 28, tb_y + 275], radius=6, fill="#f1f5f9", outline="#cbd5e1", width=1)
    draw.text((tb_x + 44, tb_y + 188), "class TextAnnotation : AnnotationBase {", fill="#0f172a", font=tb_font_code)
    draw.text((tb_x + 64, tb_y + 214), "public Rect Bounds { get; set; }", fill="#2563eb", font=tb_font_code)
    draw.text((tb_x + 64, tb_y + 240), "public FormattedText FormattedContent { get; set; }", fill="#059669", font=tb_font_code)
    draw.text((tb_x + 44, tb_y + 266), "}", fill="#0f172a", font=tb_font_code)

    # 8-Direction Handles
    handle_positions = [
        ("TL", tb_x, tb_y),
        ("T",  tb_x + tb_w//2, tb_y),
        ("TR", tb_x + tb_w, tb_y),
        ("L",  tb_x, tb_y + tb_h//2),
        ("R",  tb_x + tb_w, tb_y + tb_h//2),
        ("BL", tb_x, tb_y + tb_h),
        ("B",  tb_x + tb_w//2, tb_y + tb_h),
        ("BR", tb_x + tb_w, tb_y + tb_h),
    ]

    for label, hx, hy in handle_positions:
        # Outer handle circle
        draw.ellipse([hx - 7, hy - 7, hx + 7, hy + 7], fill="#ffffff", outline="#2563eb", width=2)
        # Inner handle dot
        draw.ellipse([hx - 3, hy - 3, hx + 3, hy + 3], fill="#2563eb")

    # Bottom Right resize cursor / indicator
    br_x, br_y = tb_x + tb_w, tb_y + tb_h
    draw.line([(br_x + 14, br_y + 14), (br_x + 30, br_y + 30)], fill="#2563eb", width=3)
    draw.polygon([(br_x + 32, br_y + 32), (br_x + 22, br_y + 32), (br_x + 32, br_y + 22)], fill="#2563eb")
    draw.text((br_x + 38, br_y + 22), "Resize (680 × 320)", fill="#2563eb", font=get_font("segoeui", 13, True))

    out_path = os.path.join(OUTPUT_DIR, "textbox-resize.webp")
    img.convert("RGB").save(out_path, "WEBP", quality=95, method=6)
    print(f"  -> Saved {out_path} ({os.path.getsize(out_path)} bytes)")


def build_dark_theme():
    print("Building dark-theme.webp (1200 x 800)...")
    target_w, target_h = 1200, 800
    # Deep obsidian background
    img = Image.new("RGBA", (target_w, target_h), "#0c141d")
    draw = ImageDraw.Draw(img)

    # Dark Toolbar Chrome
    draw.rectangle([0, 0, target_w, 64], fill="#17212c", outline="#314151", width=1)
    
    # Tools in Dark mode
    tools = [
        ("Undo", False), ("Redo", False), ("|", False),
        ("Pen", True), ("Highlighter", False), ("Eraser", False), ("Shape", False),
        ("Text", False), ("Laser", False), ("Ruler", False), ("Select", False)
    ]
    tx = 30
    for name, is_active in tools:
        if name == "|":
            draw.line([(tx + 10, 16), (tx + 10, 48)], fill="#314151", width=1)
            tx += 25
            continue
        tw = 80 if name in ["Pen", "Highlighter"] else 70
        if is_active:
            draw.rounded_rectangle([tx, 12, tx + tw, 52], radius=8, fill="#203e5c", outline="#6eacea", width=2)
            draw.text((tx + 18, 22), name, fill="#e2f0fd", font=get_font("segoeui", 14, True))
        else:
            draw.rounded_rectangle([tx, 12, tx + tw, 52], radius=8, fill=None)
            draw.text((tx + 16, 22), name, fill="#a9b5bf", font=get_font("segoeui", 14, False))
        tx += tw + 8

    # Left Sidebar in dark theme
    draw.rectangle([0, 64, 180, target_h], fill="#111a24", outline="#223343", width=1)
    draw.rounded_rectangle([18, 90, 162, 280], radius=8, fill="#17212c", outline="#6eacea", width=2)
    # Thumbnail mini preview in dark
    draw.rectangle([30, 105, 150, 115], fill="#314151")
    draw.rectangle([30, 125, 140, 132], fill="#314151")
    draw.line([(40, 170), (120, 190), (80, 220)], fill="#6eacea", width=2)
    draw.text((80, 290), "Page 1", fill="#eef2f4", font=get_font("segoeui", 12, True))

    # Dark Paper Canvas
    paper_left = 240
    paper_top = 90
    paper_right = target_w - 40
    paper_bottom = target_h + 200

    # Glow shadow
    for offset in range(12, 0, -2):
        draw.rounded_rectangle(
            [paper_left - offset, paper_top - offset//2, paper_right + offset, paper_bottom],
            radius=12,
            fill=(0, 0, 0, int(15 + offset * 3))
        )
    draw.rounded_rectangle([paper_left, paper_top, paper_right, paper_bottom], radius=10, fill="#17212c", outline="#314151", width=1)

    # Document Header & Text (Light text on dark paper)
    font_h1 = get_font("segoeui", 26, True)
    font_h2 = get_font("segoeui", 18, True)
    font_body = get_font("segoeui", 15, False)

    draw.text((paper_left + 50, paper_top + 40), "NIGHT READING & LOW-GLARE SURFACE", fill="#eef2f4", font=font_h1)
    draw.text((paper_left + 50, paper_top + 80), "Harmonious Dark Theme with Slate Backdrop", fill="#6eacea", font=font_h2)

    lines = [
        "OpenNotes dark theme is engineered for extended evening reading and writing sessions.",
        "Deep contrast ratios prevent eye fatigue while preserving sharp typographical definition.",
        "• Background: #17212c (Charcoal Slate) with 96% surface opacity.",
        "• Primary text: #eef2f4 (High luminance, anti-glare).",
        "• Luminescent ink: Neon Cyan (#6eacea) and Amber Gold (#f2c75c) for instant scanning.",
        "• System theme sync: Seamlessly follows Windows dark mode and high-contrast settings."
    ]

    curr_y = paper_top + 130
    for l in lines:
        draw.text((paper_left + 50, curr_y), l, fill="#a9b5bf", font=font_body)
        curr_y += 36

    # Glowing Ink Annotations on Dark Surface
    # Neon cyan highlight overlay
    hl_overlay = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    hl_draw = ImageDraw.Draw(hl_overlay)
    hl_draw.rounded_rectangle([paper_left + 45, paper_top + 126, paper_left + 740, paper_top + 154], radius=6, fill=(110, 172, 234, 70))
    img = Image.alpha_composite(img, hl_overlay)
    draw = ImageDraw.Draw(img)

    # Neon Ink strokes
    neon_cyan = (110, 172, 234, 255)
    neon_gold = (242, 199, 92, 255)
    
    # Underline & circle
    draw.line([(paper_left + 50, paper_top + 280), (paper_left + 400, paper_top + 280)], fill=neon_cyan, width=3)
    
    # Star & note
    draw.text((paper_left + 640, paper_top + 240), "✦ Crisp Inking in Dark Mode", fill=neon_gold, font=get_font("segoeui", 17, True))
    draw.text((paper_left + 640, paper_top + 270), "Low ocular fatigue certified", fill=neon_cyan, font=get_font("segoeui", 15, False))

    out_path = os.path.join(OUTPUT_DIR, "dark-theme.webp")
    img.convert("RGB").save(out_path, "WEBP", quality=95, method=6)
    print(f"  -> Saved {out_path} ({os.path.getsize(out_path)} bytes)")


def build_page_templates():
    print("Building page-templates.webp (1200 x 800)...")
    target_w, target_h = 1200, 800
    img = Image.new("RGBA", (target_w, target_h), "#eef2f6")
    draw = ImageDraw.Draw(img)

    # Soft ambient desktop background blur effect
    for y in range(0, target_h, 40):
        draw.line([(0, y), (target_w, y)], fill="#e2e8f0", width=1)
    for x in range(0, target_w, 40):
        draw.line([(x, 0), (x, target_h)], fill="#e2e8f0", width=1)

    # Modal Dialog Window (PageTemplatePickerWindow)
    modal_w = 980
    modal_h = 680
    modal_x = (target_w - modal_w) // 2
    modal_y = (target_h - modal_h) // 2

    # Modal Shadow
    for off in range(24, 0, -3):
        draw.rounded_rectangle([modal_x - off, modal_y - off//2, modal_x + modal_w + off, modal_y + modal_h + off], radius=24, fill=(0, 0, 0, int(3 + off * 1.5)))

    # Modal Body
    draw.rounded_rectangle([modal_x, modal_y, modal_x + modal_w, modal_y + modal_h], radius=18, fill="#ffffff", outline="#cbd5e1", width=1)

    # Header
    draw.text((modal_x + 36, modal_y + 28), "Create Notebook", fill="#0f172a", font=get_font("segoeui", 22, True))
    draw.text((modal_x + 36, modal_y + 60), "Choose a page style and template to start your notes.", fill="#64748b", font=get_font("segoeui", 14, False))
    # Close X button
    draw.text((modal_x + modal_w - 50, modal_y + 28), "✕", fill="#94a3b8", font=get_font("segoeui", 16, True))

    # 9 Template Cards in 3x3 Grid
    templates = [
        ("Blank Page", "Clean white canvas", "blank"),
        ("Cornell Notes", "Summary & cue column", "cornell"),
        ("Grid Paper", "5mm precision math grid", "grid"),
        ("Lined Paper", "Standard ruled notebook", "lined"),
        ("Dotted Matrix", "Bullet journal dot grid", "dotted"),
        ("Music Staff", "Standard five-line staves", "music"),
        ("Checklist", "Tasks & action items", "checklist"),
        ("Two Column", "Split comparison layout", "twocolumn"),
        ("Meeting Notes", "Date, attendees & notes", "meeting")
    ]

    card_w = 280
    card_h = 135
    gap_x = 24
    gap_y = 16
    grid_start_x = modal_x + 36
    grid_start_y = modal_y + 100

    for idx, (title, subtitle, style) in enumerate(templates):
        row = idx // 3
        col = idx % 3
        cx = grid_start_x + col * (card_w + gap_x)
        cy = grid_start_y + row * (card_h + gap_y)

        is_selected = (idx == 1) # Cornell selected
        if is_selected:
            draw.rounded_rectangle([cx, cy, cx + card_w, cy + card_h], radius=14, fill="#eff6ff", outline="#2563eb", width=2)
        else:
            draw.rounded_rectangle([cx, cy, cx + card_w, cy + card_h], radius=14, fill="#f8fafc", outline="#e2e8f0", width=1)

        # Mini preview thumbnail on left
        prev_x = cx + 14
        prev_y = cy + 16
        prev_w = 64
        prev_h = 88
        draw.rounded_rectangle([prev_x, prev_y, prev_x + prev_w, prev_y + prev_h], radius=4, fill="#ffffff", outline="#cbd5e1", width=1)

        # Draw style lines on mini preview
        if style == "cornell":
            # Cue line & summary line
            draw.line([(prev_x + 20, prev_y), (prev_x + 20, prev_y + 65)], fill="#93c5fd", width=1)
            draw.line([(prev_x, prev_y + 65), (prev_x + prev_w, prev_y + 65)], fill="#93c5fd", width=1)
            for ly in range(prev_y + 8, prev_y + 60, 8):
                draw.line([(prev_x + 24, ly), (prev_x + prev_w - 4, ly)], fill="#e2e8f0", width=1)
        elif style == "lined":
            for ly in range(prev_y + 10, prev_y + prev_h - 6, 8):
                draw.line([(prev_x + 6, ly), (prev_x + prev_w - 6, ly)], fill="#e2e8f0", width=1)
            draw.line([(prev_x + 14, prev_y), (prev_x + 14, prev_y + prev_h)], fill="#fca5a5", width=1)
        elif style == "grid":
            for gy in range(prev_y + 8, prev_y + prev_h, 8):
                draw.line([(prev_x + 4, gy), (prev_x + prev_w - 4, gy)], fill="#e2e8f0", width=1)
            for gx in range(prev_x + 8, prev_x + prev_w, 8):
                draw.line([(gx, prev_y + 4), (gx, prev_y + prev_h - 4)], fill="#e2e8f0", width=1)
        elif style == "dotted":
            for dy in range(prev_y + 10, prev_y + prev_h - 6, 10):
                for dx in range(prev_x + 10, prev_x + prev_w - 6, 10):
                    draw.point((dx, dy), fill="#94a3b8")
        elif style == "checklist":
            for cy_item in range(prev_y + 12, prev_y + prev_h - 10, 14):
                draw.rectangle([prev_x + 8, cy_item, prev_x + 14, cy_item + 6], outline="#94a3b8", width=1)
                draw.line([(prev_x + 20, cy_item + 3), (prev_x + prev_w - 8, cy_item + 3)], fill="#cbd5e1", width=1)
        elif style == "twocolumn":
            draw.line([(prev_x + prev_w//2, prev_y + 6), (prev_x + prev_w//2, prev_y + prev_h - 6)], fill="#cbd5e1", width=1)
            for ly in range(prev_y + 10, prev_y + prev_h - 10, 10):
                draw.line([(prev_x + 6, ly), (prev_x + prev_w//2 - 4, ly)], fill="#e2e8f0", width=1)
                draw.line([(prev_x + prev_w//2 + 4, ly), (prev_x + prev_w - 6, ly)], fill="#e2e8f0", width=1)
        elif style == "music":
            for staff in range(prev_y + 14, prev_y + prev_h - 15, 24):
                for sline in range(staff, staff + 12, 3):
                    draw.line([(prev_x + 6, sline), (prev_x + prev_w - 6, sline)], fill="#94a3b8", width=1)

        # Card Text
        text_x = prev_x + prev_w + 14
        title_color = "#1d4ed8" if is_selected else "#0f172a"
        draw.text((text_x, cy + 30), title, fill=title_color, font=get_font("segoeui", 15, True))
        draw.text((text_x, cy + 56), subtitle, fill="#64748b", font=get_font("segoeui", 12, False))
        if is_selected:
            draw.text((text_x, cy + 85), "✓ Active Selection", fill="#2563eb", font=get_font("segoeui", 12, True))

    # Bottom Footer of Dialog
    foot_y = modal_y + modal_h - 80
    draw.line([(modal_x, foot_y), (modal_x + modal_w, foot_y)], fill="#e2e8f0", width=1)
    
    # Path selector label & input
    draw.text((modal_x + 36, foot_y + 24), "Save to: C:\\Notes\\Semester_2026\\", fill="#475569", font=get_font("segoeui", 13, False))
    
    # Action buttons on right
    draw.rounded_rectangle([modal_x + modal_w - 280, foot_y + 16, modal_x + modal_w - 180, foot_y + 54], radius=8, fill="#f1f5f9", outline="#cbd5e1", width=1)
    draw.text((modal_x + modal_w - 248, foot_y + 25), "Cancel", fill="#475569", font=get_font("segoeui", 13, True))

    draw.rounded_rectangle([modal_x + modal_w - 165, foot_y + 16, modal_x + modal_w - 36, foot_y + 54], radius=8, fill="#2563eb")
    draw.text((modal_x + modal_w - 145, foot_y + 25), "Create Notebook", fill="#ffffff", font=get_font("segoeui", 13, True))

    out_path = os.path.join(OUTPUT_DIR, "page-templates.webp")
    img.convert("RGB").save(out_path, "WEBP", quality=95, method=6)
    print(f"  -> Saved {out_path} ({os.path.getsize(out_path)} bytes)")


def main():
    print(f"Output directory: {OUTPUT_DIR}")
    build_hero_editor()
    build_annotation_ink()
    build_textbox_resize()
    build_dark_theme()
    build_page_templates()
    print("All artworks successfully built!")


if __name__ == "__main__":
    main()
