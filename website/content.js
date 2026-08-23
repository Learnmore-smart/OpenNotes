(function () {
  "use strict";

  const copy = {
    en: {
      "meta.title": "OpenNotes — Open a PDF. Leave a trace.",
      "meta.description": "OpenNotes is a pen-first Windows workspace for reading, annotating, and keeping your marks with the PDF.",
      "accessibility.skip": "Skip to content",
      "brand.aria": "OpenNotes home",
      "nav.primary": "Primary navigation",
      "nav.method": "The method",
      "nav.workspace": "Workspace",
      "nav.download": "Download",
      "language.label": "Choose language",
      "theme.toggle": "Toggle light and dark theme",
      "theme.label": "Theme",
      "hero.eyebrow": "The annotated paper archive",
      "hero.title": "Open a PDF. Leave a trace.",
      "hero.lede": "Read with the page in front. Write with the tools you already understand. Keep every mark with the document.",
      "hero.cta": "Get OpenNotes",
      "hero.secondary": "See the workspace",
      "hero.meta": "Windows 10 1809+ · .NET 8 · Open source",
      "hero.meta2": "Built for the page",
      "demo.app": "OpenNotes",
      "demo.document": "FIELD NOTES",
      "demo.page": "PAGE 07",
      "demo.toolbar": "Annotation preview tools",
      "demo.canvas": "Interactive annotation preview",
      "demo.textbox": "Resizable text note",
      "demo.drag": "Move text note",
      "demo.dragging": "Text note moving — release it where the thought belongs",
      "demo.dragged": "Text note moved — type here or keep shaping the note",
      "demo.resize": "Resize note",
      "demo.resize.nw": "Resize note from top left",
      "demo.resize.n": "Resize note from top",
      "demo.resize.ne": "Resize note from top right",
      "demo.resize.e": "Resize note from right",
      "demo.resize.se": "Resize note from bottom right",
      "demo.resize.s": "Resize note from bottom",
      "demo.resize.sw": "Resize note from bottom left",
      "demo.resize.w": "Resize note from left",
      "demo.text": "A good page gives the idea somewhere to land.",
      "demo.pen": "Pen",
      "demo.highlighter": "Highlighter",
      "demo.eraser": "Eraser",
      "demo.clear": "Clear marks",
      "demo.undo": "Undo",
      "demo.ready": "Pen ready — draw anywhere on the page",
      "demo.highlighterReady": "Highlighter ready — pull a thought into focus",
      "demo.eraserReady": "Eraser ready — sweep over a mark to remove it",
      "demo.cleared": "Page cleared — choose a tool and draw again",
      "demo.undone": "Last mark undone — keep drawing when you're ready",
      "demo.undoEmpty": "Nothing to undo yet",
      "demo.textSelected": "Text note selected — type, move from the grip, or resize from the blue handles",
      "demo.textResized": "Text note selected — drag a blue handle to resize",
      "demo.textResizeKeyboard": "Text note resized — use arrow keys to adjust width and height",
      "demo.hint": "Pointer input stays in your browser",
      "manifesto.kicker": "A workspace, not a toolbox.",
      "manifesto.title": "Open the document. Let the page stay in charge.",
      "manifesto.body": "OpenNotes keeps the interface quiet and puts the document in the light. Read in one window, write with the pen you already know, and keep every mark close to the source.",
      "manifesto.quote": "The page is the product.",
      "features.kicker": "The OpenNotes rhythm",
      "features.title": "Read, mark, return.",
      "features.aside": "Three small promises for a long document.",
      "feature.ink.title": "Ink that gets out of the way",
      "feature.ink.body": "Windows Ink support, a direct pen surface, and the tools you need to underline the thought before it disappears.",
      "feature.ink.caption": "PEN / HIGHLIGHT / SHAPE",
      "feature.pages.title": "The whole PDF, in view",
      "feature.pages.body": "Tabs, recent files, thumbnails, bookmarks, and search keep the document’s shape in your head while you work.",
      "feature.pages.caption": "OPEN / FIND / RETURN",
      "feature.file.title": "A file you can take with you",
      "feature.file.body": "Annotate the PDF you already have, save it atomically, and keep your notes useful outside the app.",
      "feature.file.caption": "PDF / LOCAL / YOURS",
      "inside.kicker": "At a glance",
      "inside.title": "A calm surface for serious pages.",
      "inside.lede": "A modern Windows reading room for the moments when a document is more than a document: a class, a plan, a proof, a margin full of next steps.",
      "inside.ink": "Pen, highlighter, shapes, eraser, and selection tools.",
      "inside.navigate": "Thumbnails, bookmarks, search, and page-level focus.",
      "inside.huawei": "Comfortable stylus input, including basic Huawei M-Pencil support.",
      "inside.version": "Current release",
      "inside.platform": "Windows 10 / 11",
      "inside.format": "Native document",
      "artwork.kicker": "Leave room for your images",
      "artwork.title": "The page can show its own evidence.",
      "artwork.aside": "Replace any filename below with your approved screenshot or mark. The page keeps a quiet fallback until then.",
      "artwork.hero": "Desktop editor overview",
      "artwork.ink": "Ink tools detail",
      "artwork.textbox": "Resizable text box",
      "artwork.dark": "Dark paper and chrome",
      "artwork.templates": "Page templates",
      "artwork.mark": "Product mark",
      "artwork.caption.hero": "The editor, open and ready",
      "artwork.caption.ink": "Ink close to the thought",
      "artwork.caption.textbox": "Shape the note to the page",
      "artwork.caption.dark": "A quieter evening surface",
      "artwork.caption.templates": "Begin with a page that fits",
      "artwork.caption.mark": "The OpenNotes mark",
      "artwork.loaded": "image loaded",
      "artwork.placeholder": "placeholder — add the file to show it",
      "principles.kicker": "Design notes",
      "principles.title": "Less chrome. More contact.",
      "principle.one.title": "Paper logic",
      "principle.one.body": "Pages, margins, tabs, and marks are the structure — not a dashboard around it.",
      "principle.two.title": "Direct hands",
      "principle.two.body": "A click, a stroke, a drag. The gesture starts where your thought starts.",
      "principle.three.title": "Honest files",
      "principle.three.body": "Your PDF stays the center of gravity, ready to travel with the notes around it.",
      "download.kicker": "For the desk you already have",
      "download.title": "Make room for the page.",
      "download.body": "OpenNotes is a focused, open-source PDF annotation workspace for Windows. Bring a document in and start where the idea is.",
      "download.cta": "Download latest release",
      "download.github": "Read the repository",
      "footer.legacy": "formerly Caelum",
      "footer.github": "GitHub",
      "footer.issues": "Issues",
      "footer.license": "MIT License",
      "footer.copy": "Made for people who still think in the margins.",
      "not-found.kicker": "404 / page missing",
      "not-found.title": "This page wandered off the margin.",
      "not-found.body": "The page you were looking for is not here. The document is still on the desk.",
      "not-found.back": "Return to OpenNotes"
    },
    zh: {
      "meta.title": "OpenNotes — 打开 PDF，留下痕迹。",
      "meta.description": "OpenNotes 是一款以手写笔为先的 Windows 工作区，用来阅读和批注 PDF，并让每一笔都跟随文档。",
      "accessibility.skip": "跳到正文",
      "brand.aria": "OpenNotes 首页",
      "nav.primary": "主导航",
      "nav.method": "设计方式",
      "nav.workspace": "工作区",
      "nav.download": "下载",
      "language.label": "选择语言",
      "theme.toggle": "切换浅色和深色主题",
      "theme.label": "主题",
      "hero.eyebrow": "蓝色墨水批注档案",
      "hero.title": "打开 PDF，留下痕迹。",
      "hero.lede": "让页面始终在前。用你已经熟悉的工具书写。让每一笔都跟随文档。",
      "hero.cta": "获取 OpenNotes",
      "hero.secondary": "看看工作区",
      "hero.meta": "Windows 10 1809+ · .NET 8 · 开源",
      "hero.meta2": "为页面而生",
      "demo.app": "OpenNotes",
      "demo.document": "现场笔记",
      "demo.page": "第 07 页",
      "demo.toolbar": "批注预览工具",
      "demo.canvas": "交互式批注预览",
      "demo.textbox": "可调整大小的文本便签",
      "demo.drag": "移动文本便签",
      "demo.dragging": "正在移动文本便签 — 把它放到想法所在的位置",
      "demo.dragged": "文本便签已移动 — 在这里输入，或继续调整便签",
      "demo.resize": "调整便签大小",
      "demo.resize.nw": "从左上角调整便签大小",
      "demo.resize.n": "从上边缘调整便签大小",
      "demo.resize.ne": "从右上角调整便签大小",
      "demo.resize.e": "从右边缘调整便签大小",
      "demo.resize.se": "从右下角调整便签大小",
      "demo.resize.s": "从下边缘调整便签大小",
      "demo.resize.sw": "从左下角调整便签大小",
      "demo.resize.w": "从左边缘调整便签大小",
      "demo.text": "好页面会给想法留一个落脚处。",
      "demo.pen": "画笔",
      "demo.highlighter": "荧光笔",
      "demo.eraser": "橡皮擦",
      "demo.clear": "清除笔迹",
      "demo.undo": "撤销",
      "demo.ready": "画笔已就绪 — 在页面上任意书写",
      "demo.highlighterReady": "荧光笔已就绪 — 把想法标记出来",
      "demo.eraserReady": "橡皮擦已就绪 — 在笔迹上划过即可删除",
      "demo.cleared": "页面已清空 — 选择工具重新书写",
      "demo.undone": "已撤销上一笔 — 准备好后继续书写",
      "demo.undoEmpty": "暂时没有可撤销的笔迹",
      "demo.textSelected": "文本便签已选中 — 输入文字、拖动手柄移动，或从蓝色圆点调整大小",
      "demo.textResized": "文本便签已选中 — 拖动任意蓝色手柄调整大小",
      "demo.textResizeKeyboard": "文本便签已调整 — 使用方向键调整宽度和高度",
      "demo.hint": "指针输入只留在浏览器中",
      "manifesto.kicker": "一处工作区，不是一整套工具箱。",
      "manifesto.title": "打开文档，让页面重新掌握节奏。",
      "manifesto.body": "OpenNotes 让界面安静下来，把文档放在光里。在一个窗口中阅读，用熟悉的手写笔留下想法，让每一笔都靠近它的来源。",
      "manifesto.quote": "页面本身就是产品。",
      "features.kicker": "OpenNotes 的节奏",
      "features.title": "阅读，标记，再回来。",
      "features.aside": "给长文档的三个小承诺。",
      "feature.ink.title": "让墨迹退到一旁",
      "feature.ink.body": "支持 Windows Ink，提供直接的手写表面，以及在想法消失前划出重点所需的工具。",
      "feature.ink.caption": "画笔 / 高亮 / 形状",
      "feature.pages.title": "整份 PDF，都在眼前",
      "feature.pages.body": "标签页、最近文件、缩略图、书签和搜索，让你工作时始终记得文档的整体形状。",
      "feature.pages.caption": "打开 / 查找 / 返回",
      "feature.file.title": "带得走的文件",
      "feature.file.body": "批注你已有的 PDF，以安全方式保存，并让笔记离开应用后依然有用。",
      "feature.file.caption": "PDF / 本地 / 属于你",
      "inside.kicker": "一眼看懂",
      "inside.title": "为重要页面留出安静的表面。",
      "inside.lede": "一间现代 Windows 阅读室，适合那些不止是一份文档的时刻：课程、计划、校样，以及写满下一步的页边。",
      "inside.ink": "画笔、荧光笔、形状、橡皮擦和选择工具。",
      "inside.navigate": "缩略图、书签、搜索，以及专注于当前页面。",
      "inside.huawei": "自然的手写笔输入，包含华为 M-Pencil 的基本支持。",
      "inside.version": "当前版本",
      "inside.platform": "Windows 10 / 11",
      "inside.format": "原生文档",
      "artwork.kicker": "为你的图片留出位置",
      "artwork.title": "让页面展示自己的证据。",
      "artwork.aside": "把下面的文件名替换成你认可的截图或标志；在替换前，页面会保持安静的占位状态。",
      "artwork.hero": "桌面编辑器概览",
      "artwork.ink": "墨迹工具细节",
      "artwork.textbox": "可调整大小的文本框",
      "artwork.dark": "深色纸张与界面",
      "artwork.templates": "页面模板",
      "artwork.mark": "产品标志",
      "artwork.caption.hero": "编辑器已打开，随时书写",
      "artwork.caption.ink": "让墨迹靠近想法",
      "artwork.caption.textbox": "让笔记适应页面",
      "artwork.caption.dark": "更安静的夜间表面",
      "artwork.caption.templates": "从合适的页面开始",
      "artwork.caption.mark": "OpenNotes 标志",
      "artwork.loaded": "图片已加载",
      "artwork.placeholder": "占位图 — 添加同名文件即可显示",
      "principles.kicker": "设计笔记",
      "principles.title": "少一点界面，多一点接触。",
      "principle.one.title": "纸张逻辑",
      "principle.one.body": "页面、页边、标签和笔迹就是结构，不需要一层仪表盘包围它。",
      "principle.two.title": "直接的手",
      "principle.two.body": "一次点击、一笔书写、一次拖动。动作从想法开始的地方开始。",
      "principle.three.title": "诚实的文件",
      "principle.three.body": "PDF 始终是中心，笔记围绕它一起走向下一张桌子。",
      "download.kicker": "就在你已有的桌面上",
      "download.title": "给页面腾出位置。",
      "download.body": "OpenNotes 是一款专注、开源的 Windows PDF 批注工作区。把文档带进来，从想法所在的地方开始。",
      "download.cta": "下载最新版本",
      "download.github": "阅读代码仓库",
      "footer.legacy": "原名 Caelum",
      "footer.github": "GitHub",
      "footer.issues": "问题反馈",
      "footer.license": "MIT 许可证",
      "footer.copy": "为仍然相信页边有意义的人制作。",
      "not-found.kicker": "404 / 页面走失",
      "not-found.title": "这页走出了页边。",
      "not-found.body": "你要找的页面不在这里，但文档还在桌面上。",
      "not-found.back": "返回 OpenNotes"
    },
    fr: {
      "meta.title": "OpenNotes — Ouvrez un PDF. Laissez une trace.",
      "meta.description": "OpenNotes est un espace Windows pensé pour le stylet, pour lire et annoter vos PDF sans séparer les marques du document.",
      "accessibility.skip": "Aller au contenu",
      "brand.aria": "Accueil OpenNotes",
      "nav.primary": "Navigation principale",
      "nav.method": "La méthode",
      "nav.workspace": "Espace de travail",
      "nav.download": "Télécharger",
      "language.label": "Choisir la langue",
      "theme.toggle": "Changer entre les thèmes clair et sombre",
      "theme.label": "Thème",
      "hero.eyebrow": "L’archive de papier annoté",
      "hero.title": "Ouvrez un PDF. Laissez une trace.",
      "hero.lede": "Gardez la page devant vous. Écrivez avec des outils familiers. Laissez chaque marque suivre le document.",
      "hero.cta": "Obtenir OpenNotes",
      "hero.secondary": "Voir l’espace de travail",
      "hero.meta": "Windows 10 1809+ · .NET 8 · Open source",
      "hero.meta2": "Fait pour la page",
      "demo.app": "OpenNotes",
      "demo.document": "NOTES DE TERRAIN",
      "demo.page": "PAGE 07",
      "demo.toolbar": "Outils de prévisualisation",
      "demo.canvas": "Aperçu interactif des annotations",
      "demo.textbox": "Note texte redimensionnable",
      "demo.drag": "Déplacer la note texte",
      "demo.dragging": "Note en mouvement — relâchez-la là où l’idée doit rester",
      "demo.dragged": "Note déplacée — écrivez ici ou continuez à la façonner",
      "demo.resize": "Redimensionner la note",
      "demo.resize.nw": "Redimensionner depuis le coin supérieur gauche",
      "demo.resize.n": "Redimensionner depuis le bord supérieur",
      "demo.resize.ne": "Redimensionner depuis le coin supérieur droit",
      "demo.resize.e": "Redimensionner depuis le bord droit",
      "demo.resize.se": "Redimensionner depuis le coin inférieur droit",
      "demo.resize.s": "Redimensionner depuis le bord inférieur",
      "demo.resize.sw": "Redimensionner depuis le coin inférieur gauche",
      "demo.resize.w": "Redimensionner depuis le bord gauche",
      "demo.text": "Une bonne page offre un endroit où l’idée peut se poser.",
      "demo.pen": "Stylet",
      "demo.highlighter": "Surligneur",
      "demo.eraser": "Gomme",
      "demo.clear": "Effacer les marques",
      "demo.undo": "Annuler",
      "demo.ready": "Stylet prêt — dessinez sur la page",
      "demo.highlighterReady": "Surligneur prêt — faites ressortir une idée",
      "demo.eraserReady": "Gomme prête — passez sur une marque pour la retirer",
      "demo.cleared": "Page effacée — choisissez un outil et dessinez à nouveau",
      "demo.undone": "Dernière marque annulée — reprenez quand vous êtes prêt",
      "demo.undoEmpty": "Rien à annuler pour l’instant",
      "demo.textSelected": "Note sélectionnée — écrivez, déplacez-la avec la poignée ou redimensionnez-la avec les points bleus",
      "demo.textResized": "Note texte sélectionnée — faites glisser une poignée bleue pour redimensionner",
      "demo.textResizeKeyboard": "Note texte redimensionnée — utilisez les flèches pour ajuster largeur et hauteur",
      "demo.hint": "Les gestes restent dans votre navigateur",
      "manifesto.kicker": "Un espace de travail, pas une boîte à outils.",
      "manifesto.title": "Ouvrez le document. Laissez la page mener la danse.",
      "manifesto.body": "OpenNotes apaise l’interface et met le document en lumière. Lisez dans une fenêtre, écrivez avec le stylet que vous connaissez, et gardez chaque marque près de sa source.",
      "manifesto.quote": "La page est le produit.",
      "features.kicker": "Le rythme OpenNotes",
      "features.title": "Lire, marquer, revenir.",
      "features.aside": "Trois petites promesses pour les longs documents.",
      "feature.ink.title": "L’encre à sa juste place",
      "feature.ink.body": "Windows Ink, une surface de stylet directe et les outils pour souligner l’idée avant qu’elle ne file.",
      "feature.ink.caption": "STYLET / SURBRILLANCE / FORME",
      "feature.pages.title": "Tout le PDF, sous les yeux",
      "feature.pages.body": "Onglets, fichiers récents, miniatures, signets et recherche gardent la forme du document présente pendant le travail.",
      "feature.pages.caption": "OUVRIR / TROUVER / REVENIR",
      "feature.file.title": "Un fichier qui vous suit",
      "feature.file.body": "Annotez le PDF que vous avez déjà, enregistrez-le proprement et gardez vos notes utiles hors de l’application.",
      "feature.file.caption": "PDF / LOCAL / À VOUS",
      "inside.kicker": "En un coup d’œil",
      "inside.title": "Une surface calme pour les pages qui comptent.",
      "inside.lede": "Une salle de lecture Windows moderne pour les moments où un document devient plus qu’un document : un cours, un plan, une épreuve, une marge pleine de prochaines étapes.",
      "inside.ink": "Stylet, surligneur, formes, gomme et outils de sélection.",
      "inside.navigate": "Miniatures, signets, recherche et attention portée à la page active.",
      "inside.huawei": "Une saisie au stylet naturelle, avec la prise en charge de base du Huawei M-Pencil.",
      "inside.version": "Version actuelle",
      "inside.platform": "Windows 10 / 11",
      "inside.format": "Document natif",
      "artwork.kicker": "Laissez une place à vos images",
      "artwork.title": "La page peut montrer ses propres preuves.",
      "artwork.aside": "Remplacez chaque nom ci-dessous par une capture ou une marque approuvée. En attendant, la page garde un emplacement discret.",
      "artwork.hero": "Vue d’ensemble de l’éditeur",
      "artwork.ink": "Détail des outils d’encre",
      "artwork.textbox": "Zone de texte redimensionnable",
      "artwork.dark": "Papier et interface sombres",
      "artwork.templates": "Modèles de page",
      "artwork.mark": "Marque du produit",
      "artwork.caption.hero": "L’éditeur, ouvert et prêt",
      "artwork.caption.ink": "L’encre près de la pensée",
      "artwork.caption.textbox": "Façonnez la note à la page",
      "artwork.caption.dark": "Une surface plus calme le soir",
      "artwork.caption.templates": "Commencez par la bonne page",
      "artwork.caption.mark": "La marque OpenNotes",
      "artwork.loaded": "image chargée",
      "artwork.placeholder": "emplacement — ajoutez le fichier pour l’afficher",
      "principles.kicker": "Notes de design",
      "principles.title": "Moins d’interface. Plus de contact.",
      "principle.one.title": "Logique du papier",
      "principle.one.body": "Pages, marges, onglets et marques forment la structure — pas un tableau de bord autour d’elle.",
      "principle.two.title": "Mains directes",
      "principle.two.body": "Un clic, un trait, un glissement. Le geste commence là où commence la pensée.",
      "principle.three.title": "Fichiers honnêtes",
      "principle.three.body": "Votre PDF reste le centre de gravité, prêt à voyager avec les notes qui l’entourent.",
      "download.kicker": "Pour le bureau que vous avez déjà",
      "download.title": "Faites de la place à la page.",
      "download.body": "OpenNotes est un espace open source, concentré sur l’annotation PDF sous Windows. Apportez un document et commencez là où vit l’idée.",
      "download.cta": "Télécharger la dernière version",
      "download.github": "Lire le dépôt",
      "footer.legacy": "anciennement Caelum",
      "footer.github": "GitHub",
      "footer.issues": "Issues",
      "footer.license": "Licence MIT",
      "footer.copy": "Fait pour celles et ceux qui pensent encore dans les marges.",
      "not-found.kicker": "404 / page absente",
      "not-found.title": "Cette page a dépassé la marge.",
      "not-found.body": "La page recherchée n’est pas ici. Le document, lui, est toujours sur le bureau.",
      "not-found.back": "Retourner à OpenNotes"
    }
  };

  const storageKey = "opennotes-locale";
  const supportedLocales = Object.keys(copy);

  function getStoredLocale() {
    try {
      const stored = window.localStorage.getItem(storageKey);
      if (supportedLocales.includes(stored)) {
        return stored;
      }

      const preferred = (navigator.languages || [navigator.language || "en"])
        .map((locale) => locale.toLowerCase())
        .find((locale) => locale.startsWith("zh") || locale.startsWith("fr") || locale.startsWith("en"));
      return preferred?.startsWith("zh") ? "zh" : preferred?.startsWith("fr") ? "fr" : "en";
    } catch (_error) {
      return "en";
    }
  }

  function setText(key, locale) {
    const value = copy[locale][key];
    if (value === undefined) {
      return;
    }

    document.querySelectorAll(`[data-i18n="${key}"]`).forEach((node) => {
      node.textContent = value;
    });

    document.querySelectorAll(`[data-i18n-html="${key}"]`).forEach((node) => {
      node.innerHTML = value;
    });

    document.querySelectorAll(`[data-i18n-content="${key}"]`).forEach((node) => {
      node.setAttribute("content", value);
    });

    document.querySelectorAll(`[data-i18n-aria-label="${key}"]`).forEach((node) => {
      node.setAttribute("aria-label", value);
    });
  }

  function setLocale(nextLocale) {
    const locale = supportedLocales.includes(nextLocale) ? nextLocale : "en";
    const localeCopy = copy[locale];

    Object.keys(localeCopy).forEach((key) => setText(key, locale));
    document.documentElement.lang = locale;
    document.documentElement.dataset.locale = locale;

    document.querySelectorAll("button[data-locale]").forEach((button) => {
      const isActive = button.dataset.locale === locale;
      button.classList.toggle("is-active", isActive);
      button.setAttribute("aria-pressed", String(isActive));
    });

    try {
      window.localStorage.setItem(storageKey, locale);
    } catch (_error) {
      // Language switching still works when storage is unavailable.
    }

    document.dispatchEvent(new CustomEvent("opennotes:localechange", { detail: { locale } }));
  }

  function init() {
    document.querySelectorAll("button[data-locale]").forEach((button) => {
      button.addEventListener("click", () => setLocale(button.dataset.locale));
    });

    setLocale(getStoredLocale());
  }

  window.OpenNotesI18n = { copy, setLocale };
  init();
})();
