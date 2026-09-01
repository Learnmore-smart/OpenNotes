using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Caelum.Models;

namespace Caelum.Services
{
    public static class LocalizationService
    {
        private static readonly IReadOnlyList<LanguageOption> LanguageOptions = new[]
        {
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.Chinese, "\u4E2D\u6587"),
            new LanguageOption(AppLanguage.French, "Fran\u00E7ais")
        };

        private static readonly Dictionary<string, (string English, string Chinese, string French)> Strings = new()
        {
            ["Common.Cancel"] = ("Cancel", "\u53D6\u6D88", "Annuler"),
            ["Common.Error"] = ("Error", "\u9519\u8BEF", "Erreur"),
            ["Common.OK"] = ("OK", "\u786E\u5B9A", "OK"),
            ["Common.Save"] = ("Save", "\u4FDD\u5B58", "Enregistrer"),
            ["Common.Browse"] = ("Browse", "\u6D4F\u89C8", "Parcourir"),
            ["Common.Close"] = ("Close", "\u5173\u95ED", "Fermer"),
            ["Editor.AutoSaved"] = ("Auto-saved", "\u5DF2\u81EA\u52A8\u4FDD\u5B58", "Enregistr\u00E9 automatiquement"),
            ["Editor.DeleteTooltip"] = ("Delete", "\u5220\u9664", "Supprimer"),
            ["Editor.EraserTooltip"] = ("Eraser", "\u6A61\u76AE\u64E6", "Gomme"),
            ["Editor.HighlighterTooltip"] = ("Highlighter", "\u8357\u5149\u7B14", "Surligneur"),
            ["Editor.AddPageFailed"] = ("Failed to add a page: {0}", "\u65E0\u6CD5\u6DFB\u52A0\u9875\u9762\uFF1A{0}", "\u00C9chec de l'ajout d'une page : {0}"),
            ["Editor.AddPageTooltip"] = ("Add page", "\u6DFB\u52A0\u9875\u9762", "Ajouter une page"),
            ["Editor.DeletePageFailed"] = ("Failed to delete the page: {0}", "\u65E0\u6CD5\u5220\u9664\u9875\u9762\uFF1A{0}", "\u00C9chec de la suppression de la page : {0}"),
            ["Editor.DeletePageTooltip"] = ("Delete page", "\u5220\u9664\u9875\u9762", "Supprimer la page"),
            ["Editor.InsertPageDialogTitle"] = ("Insert page", "\u63D2\u5165\u9875\u9762", "Ins\u00E9rer une page"),
            ["Editor.InsertPageDialogSubtitle"] = ("Choose the page style to insert at this position.", "\u9009\u62E9\u8981\u63D2\u5165\u5230\u6B64\u5904\u7684\u9875\u9762\u6837\u5F0F\u3002", "Choisissez le style de page \u00E0 ins\u00E9rer \u00E0 cet emplacement."),
            ["Editor.InsertPageHereTooltip"] = ("Insert page here", "\u5728\u6B64\u5904\u63D2\u5165\u9875\u9762", "Ins\u00E9rer une page ici"),
            ["Editor.Loading"] = ("Loading...", "\u52A0\u8F7D\u4E2D...", "Chargement..."),
            ["Editor.ModeEraser"] = ("Eraser", "\u6A61\u76AE\u64E6", "Gomme"),
            ["Editor.ModeHighlighter"] = ("Highlighter", "\u8357\u5149\u7B14", "Surligneur"),
            ["Editor.ModeHiddenInk"] = ("Hidden ink", "\u9690\u85CF\u58A8\u8FF9", "Encre masquée"),
            ["Editor.ModePen"] = ("Pen", "\u753B\u7B14", "Stylo"),
            ["Editor.ModeSelect"] = ("Select", "\u9009\u62E9", "S\u00E9lection"),
            ["Editor.ModeText"] = ("Text", "\u6587\u672C", "Texte"),
            ["Editor.NoDocumentLoaded"] = ("No PDF is currently loaded", "\u5F53\u524D\u6CA1\u6709\u6253\u5F00 PDF", "Aucun PDF n'est ouvert"),
            ["Editor.PageAdded"] = ("Page added", "\u5DF2\u6DFB\u52A0\u9875\u9762", "Page ajout\u00E9e"),
            ["Editor.PageDeleted"] = ("Page deleted", "\u9875\u9762\u5DF2\u5220\u9664", "Page supprim\u00E9e"),
            ["Editor.PageDeleteBlocked"] = ("The document must keep at least one page", "\u6587\u6863\u81F3\u5C11\u9700\u8981\u4FDD\u7559\u4E00\u9875", "Le document doit conserver au moins une page"),
            ["Editor.PageJumpTooltip"] = ("Click to jump to a page", "\u70B9\u51FB\u8DF3\u8F6C\u5230\u6307\u5B9A\u9875", "Cliquer pour aller \u00E0 une page"),
            ["Editor.PreviousPage"] = ("Previous page", "\u4E0A\u4E00\u9875", "Page pr\u00E9c\u00E9dente"),
            ["Editor.NextPage"] = ("Next page", "\u4E0B\u4E00\u9875", "Page suivante"),
            ["Editor.PageJumpInvalid"] = ("Enter a whole page number.", "\u8BF7\u8F93\u5165\u6574\u6570\u9875\u7801\u3002", "Saisissez un numéro de page entier."),
            ["Editor.PageJumpOutOfRange"] = ("Page adjusted; enter a page number between 1 and {0}.", "\u9875\u7801\u5DF2\u8C03\u6574\uFF1B\u8BF7\u8F93\u5165 1 \u5230 {0} \u4E4B\u95F4\u7684\u9875\u7801\u3002", "Page ajustée ; saisissez un numéro entre 1 et {0}."),
            ["Editor.PageTemplateBlank"] = ("Blank", "\u7A7A\u767D\u9875", "Vierge"),
            ["Editor.PageTemplateBlankHint"] = ("Plain white paper for sketches or mixed notes.", "\u9002\u5408\u7D20\u63CF\u6216\u6DF7\u5408\u8BB0\u5F55\u7684\u7EAF\u767D\u9875\u9762\u3002", "Une feuille blanche simple pour dessiner ou prendre des notes libres."),
            ["Editor.PageTemplateLined"] = ("Lined", "\u6A2A\u7EBF\u9875", "Lign\u00E9e"),
            ["Editor.PageTemplateLinedHint"] = ("Even horizontal rules for clean handwriting.", "\u5747\u5300\u7684\u6A2A\u5411\u7EBF\u6761\uFF0C\u4FBF\u4E8E\u6574\u9F50\u4E66\u5199\u3002", "Des lignes horizontales r\u00E9guli\u00E8res pour une \u00E9criture soign\u00E9e."),
            ["Editor.PageTemplateNotebook"] = ("Notebook", "\u7B14\u8BB0\u672C\u9875", "Carnet"),
            ["Editor.PageTemplateNotebookHint"] = ("Warm paper with a margin line and notebook rules.", "\u5E26\u5DE6\u4FA7\u8FB9\u7EBF\u548C\u7B14\u8BB0\u672C\u6A2A\u7EBF\u7684\u6696\u8272\u9875\u9762\u3002", "Une page chaude avec marge rouge et lignes de carnet."),
            ["Editor.PageTemplateQuadrille"] = ("Quadrille", "\u65B9\u683C\u9875", "Quadrill\u00E9e"),
            ["Editor.PageTemplateQuadrilleHint"] = ("Grid paper for diagrams, layouts, and math.", "\u9002\u5408\u7ED8\u56FE\u3001\u5E03\u5C40\u548C\u7B97\u5F0F\u7684\u65B9\u683C\u7EB8\u3002", "Une grille pour les sch\u00E9mas, les mises en page et les calculs."),
            ["Editor.PenTooltip"] = ("Pen", "\u753B\u7B14", "Stylo"),
            ["Editor.SelectTooltip"] = ("Select and Transform", "\u9009\u62E9\u5E76\u53D8\u6362", "S\u00E9lectionner et transformer"),
            ["Editor.SelectFilter"] = ("Select", "\u9009\u62E9\u5185\u5BB9", "Filtrer"),
            ["Editor.SelectFilterBoth"] = ("Both", "\u5168\u90E8", "Tous"),
            ["Editor.SelectFilterDrawings"] = ("Drawings", "\u56FE\u5F62", "Dessins"),
            ["Editor.SelectFilterText"] = ("Text", "\u6587\u672C", "Texte"),
            ["Editor.SelectedDrawingStyle"] = ("Selected drawing", "\u5DF2\u9009\u56FE\u5F62", "Dessin s\u00E9lectionn\u00E9"),
            ["Editor.SelectShape"] = ("Shape", "\u9009\u62E9\u65B9\u5F0F", "Forme"),
            ["Editor.SelectShapeRect"] = ("Rectangle", "\u77E9\u5F62\u9009\u62E9", "Rectangle"),
            ["Editor.SelectShapeFree"] = ("Freehand", "\u81EA\u7531\u9009\u62E9", "Main lev\u00E9e"),
            ["Editor.PopupColor"] = ("Color", "\u989C\u8272", "Couleur"),
            ["Editor.PopupEraserSize"] = ("Eraser size", "\u6A61\u76AE\u64E6\u5927\u5C0F", "Taille de la gomme"),
            ["Editor.PopupPreview"] = ("Preview", "\u9884\u89C8", "Aper\u00E7u"),
            ["Editor.PopupSize"] = ("Size", "\u5927\u5C0F", "Taille"),
            ["Editor.RedoTooltip"] = ("Redo (Ctrl+Y)", "\u91CD\u505A (Ctrl+Y)", "R\u00E9tablir (Ctrl+Y)"),
            ["Editor.SaveTooltip"] = ("Save", "\u4FDD\u5B58", "Enregistrer"),
            ["Editor.TextTooltip"] = ("Text", "\u6587\u672C", "Texte"),
            ["Editor.UndoTooltip"] = ("Undo (Ctrl+Z)", "\u64A4\u9500 (Ctrl+Z)", "Annuler (Ctrl+Z)"),
            ["Editor.ZoomEditTooltip"] = ("Click to set zoom", "\u70B9\u51FB\u8BBE\u7F6E\u7F29\u653E", "Cliquer pour r\u00E9gler le zoom"),
            ["Editor.ZoomInTooltip"] = ("Zoom in", "\u653E\u5927", "Zoom avant"),
            ["Editor.ZoomOutTooltip"] = ("Zoom out", "\u7F29\u5C0F", "Zoom arri\u00E8re"),
            ["Editor.ResizeTextBox"] = ("Resize text box", "\u8C03\u6574\u6587\u672C\u6846\u5927\u5C0F", "Redimensionner la zone de texte"),
            ["Editor.MoveTextBox"] = ("Move text box", "\u79FB\u52A8\u6587\u672C\u6846", "D\u00E9placer la zone de texte"),
            ["Editor.ModeShape"] = ("Shape", "\u5F62\u72B6", "Forme"),
            ["Editor.ModeLaser"] = ("Laser pointer", "\u6FC0\u5149\u7B14", "Pointeur laser"),
            ["Editor.Stylus"] = ("Stylus", "\u624B\u5199\u7B14", "Stylet"),
            ["Editor.PenFeaturePressure"] = ("pressure", "\u538B\u611F", "pression"),
            ["Editor.PenFeatureTilt"] = ("tilt", "\u503E\u659C", "inclinaison"),
            ["Editor.PenFeatureBarrel"] = ("barrel button", "\u7B14\u8EAB\u6309\u952E", "bouton du stylet"),
            ["Editor.SearchResults"] = ("{0} results · F3 next / Shift+F3 previous", "{0} \u4E2A\u7ED3\u679C · F3 \u4E0B\u4E00\u4E2A / Shift+F3 \u4E0A\u4E00\u4E2A", "{0} résultats · F3 suivant / Maj+F3 précédent"),
            ["Editor.PageNumber"] = ("Page {0}", "\u7B2C {0} \u9875", "Page {0}"),
            ["Editor.LoadPdfFailed"] = ("Failed to load PDF: {0}", "\u52A0\u8F7D PDF \u5931\u8D25\uFF1A{0}", "Échec du chargement du PDF : {0}"),
            ["Editor.ErrorDetails"] = ("Details: {0}", "\u8BE6\u7EC6\u4FE1\u606F\uFF1A{0}", "Détails : {0}"),
            ["Editor.InsertBlankPageBefore"] = ("Insert blank page before", "\u5728\u6B64\u9875\u524D\u63D2\u5165\u7A7A\u767D\u9875", "Insérer une page vierge avant"),
            ["Editor.DuplicatePage"] = ("Duplicate page", "\u590D\u5236\u9875\u9762", "Dupliquer la page"),
            ["Editor.DeletePage"] = ("Delete page", "\u5220\u9664\u9875\u9762", "Supprimer la page"),
            ["Editor.PageReorderFailed"] = ("Page reorder failed: {0}", "\u9875\u9762\u91CD\u6392\u5931\u8D25\uFF1A{0}", "Échec du réordonnancement des pages : {0}"),
            ["Editor.PageDuplicateFailed"] = ("Page duplication failed: {0}", "\u590D\u5236\u9875\u9762\u5931\u8D25\uFF1A{0}", "Échec de la duplication de la page : {0}"),
            ["Editor.RemoveBookmark"] = ("Remove bookmark", "\u5220\u9664\u4E66\u7B7E", "Supprimer le signet"),
            ["Editor.UnbookmarkCurrentPage"] = ("Remove bookmark from current page", "\u53D6\u6D88\u5F53\u524D\u9875\u6536\u85CF", "Retirer le signet de la page actuelle"),
            ["Editor.SidebarExpand"] = ("Expand panel", "\u5C55\u5F00\u9762\u677F", "Développer le panneau"),
            ["Editor.PngFolderDescription"] = ("Choose a folder for PNG export", "\u9009\u62E9 PNG \u5BFC\u51FA\u6587\u4EF6\u5939", "Choisir un dossier pour l’export PNG"),
            ["Editor.PngFileFilter"] = ("PNG image|*.png", "PNG \u56FE\u7247|*.png", "Image PNG|*.png"),
            ["Editor.PdfFileFilter"] = ("PDF files|*.pdf", "PDF \u6587\u4EF6|*.pdf", "Fichiers PDF|*.pdf"),
            ["Editor.ImageFileFilter"] = ("Images|*.png;*.jpg;*.jpeg;*.bmp", "\u56FE\u7247|*.png;*.jpg;*.jpeg;*.bmp", "Images|*.png;*.jpg;*.jpeg;*.bmp"),
            ["Editor.PageRangeTitle"] = ("Choose page range", "\u9009\u62E9\u9875\u7801\u8303\u56F4", "Choisir une plage de pages"),
            ["Editor.PageRangePrompt"] = ("Enter a page range (1-{0}), for example 2-4:", "\u8F93\u5165\u9875\u7801\u8303\u56F4\uFF081-{0}\uFF09\uFF0C\u4F8B\u5982 2-4\uFF1A", "Saisissez une plage de pages (1-{0}), par exemple 2-4 :"),
            ["Editor.PdfPagesInserted"] = ("PDF pages inserted", "PDF \u9875\u9762\u5DF2\u63D2\u5165", "Pages PDF insérées"),
            ["Editor.ImagePageInserted"] = ("Image page inserted", "\u56FE\u7247\u9875\u9762\u5DF2\u63D2\u5165", "Page image insérée"),
            ["Editor.ImportFailed"] = ("Import failed: {0}", "\u5BFC\u5165\u5931\u8D25\uFF1A{0}", "Échec de l’importation : {0}"),
            ["Editor.NoPagesToPrint"] = ("The document has no pages to print.", "\u6587\u6863\u6CA1\u6709\u53EF\u6253\u5370\u7684\u9875\u9762\u3002", "Le document ne contient aucune page à imprimer."),
            ["Editor.BookmarkPage"] = ("Page {0}", "\u7B2C {0} \u9875", "Page {0}"),
            ["PageTemplate.DottedTitle"] = ("Dotted", "\u70B9\u9635", "Pointillée"),
            ["PageTemplate.DottedHint"] = ("For free notes and design sketches", "\u9002\u5408\u81EA\u7531\u7B14\u8BB0\u4E0E\u8BBE\u8BA1\u8349\u56FE", "Pour les notes libres et les croquis"),
            ["PageTemplate.MusicTitle"] = ("Music", "\u4E94\u7EBF\u8C31", "Musique"),
            ["PageTemplate.MusicHint"] = ("Staff-lined pages for music", "\u4E94\u7EBF\u8C31\u5206\u7EC4\u9875\u9762", "Pages à portée musicale"),
            ["PageTemplate.CornellTitle"] = ("Cornell", "\u5EB7\u5948\u5C14", "Cornell"),
            ["PageTemplate.CornellHint"] = ("Sections for cues, notes, and summary", "\u7EBF\u7D22\u3001\u7B14\u8BB0\u4E0E\u603B\u7ED3\u5206\u533A", "Sections pour indices, notes et résumé"),
            ["PageTemplate.ChecklistTitle"] = ("Checklist", "\u5F85\u529E\u6E05\u5355", "Liste de tâches"),
            ["PageTemplate.ChecklistHint"] = ("Checkbox rows for tasks and quick tracking", "\u7528\u4E8E\u4EFB\u52A1\u548C\u5FEB\u901F\u8DDF\u8E2A\u7684\u590D\u9009\u6846\u884C", "Lignes à cocher pour les tâches et le suivi rapide"),
            ["PageTemplate.TwoColumnTitle"] = ("Two column", "\u53CC\u680F\u7B14\u8BB0", "Deux colonnes"),
            ["PageTemplate.TwoColumnHint"] = ("Side-by-side space for comparisons and parallel notes", "\u5E76\u6392\u8BB0\u5F55\u5BF9\u6BD4\u5185\u5BB9\u6216\u53CC\u7EBF\u7B14\u8BB0", "Deux espaces côte à côte pour comparer ou noter en parallèle"),
            ["Editor.Copy"] = ("Copied", "\u5DF2\u590D\u5236", "Copié"),
            ["Editor.TextCopied"] = ("Text copied", "\u6587\u672C\u5DF2\u590D\u5236", "Texte copié"),
            ["Editor.Cut"] = ("Cut", "\u5DF2\u526A\u5207", "Coupé"),
            ["Editor.SelectionCopied"] = ("Selection copied", "\u9009\u4E2D\u5185\u5BB9\u5DF2\u590D\u5236", "Sélection copiée"),
            ["Editor.SelectionPasted"] = ("Selection pasted", "\u9009\u4E2D\u5185\u5BB9\u5DF2\u7C98\u8D34", "Sélection collée"),
            ["Editor.ImagePasted"] = ("Image pasted", "\u56FE\u7247\u5DF2\u7C98\u8D34", "Image collée"),
            ["Editor.ImageAdded"] = ("Image added", "\u56FE\u7247\u5DF2\u63D2\u5165", "Image ajoutée"),
            ["Editor.Duplicated"] = ("Duplicated", "\u5DF2\u91CD\u590D", "Dupliqué"),
            ["Editor.PenDetected"] = ("{0} pen detected{1}", "\u68C0\u6D4B\u5230 {0} \u7B14{1}", "Stylet {0} détecté{1}"),
            ["Editor.ShapeHeader"] = ("Shape", "\u5F62\u72B6", "Forme"),
            ["Editor.ShapeLine"] = ("Line", "\u76F4\u7EBF", "Ligne"),
            ["Editor.ShapeLineStyleHeader"] = ("Line style", "\u7EBF\u578B", "Style de ligne"),
            ["Editor.ShapeSolid"] = ("Solid", "\u5B9E\u7EBF", "Continue"),
            ["Editor.ShapeDashed"] = ("Dashed", "\u865A\u7EBF", "Pointill\u00E9e"),
            ["Editor.ShapeRectangle"] = ("Rectangle", "\u77E9\u5F62", "Rectangle"),
            ["Editor.ShapeEllipse"] = ("Ellipse", "\u692D\u5706", "Ellipse"),
            ["Editor.ShapeArrow"] = ("Arrow", "\u7BAD\u5934", "Flèche"),
            ["Editor.ShapeTriangle"] = ("Triangle", "\u4E09\u89D2\u5F62", "Triangle"),
            ["Editor.ShapeDiamond"] = ("Diamond", "\u83F1\u5F62", "Losange"),
            ["Editor.ShapeParallelogram"] = ("Parallelogram", "\u5E73\u884C\u56DB\u8FB9\u5F62", "Parallélogramme"),
            ["Editor.ShapePentagon"] = ("Pentagon", "\u4E94\u8FB9\u5F62", "Pentagone"),
            ["Editor.ShapeHexagon"] = ("Hexagon", "\u516D\u8FB9\u5F62", "Hexagone"),
            ["Editor.EraserModeHeader"] = ("Eraser mode", "\u64E6\u9664\u6A21\u5F0F", "Mode gomme"),
            ["Editor.EraserPixel"] = ("Pixel erase", "\u50CF\u7D20\u64E6\u9664", "Effacer les pixels"),
            ["Editor.EraserStroke"] = ("Whole stroke", "\u6574\u7B14\u64E6\u9664", "Trait entier"),
            ["Editor.HighlighterModeHeader"] = ("Mode", "\u6A21\u5F0F", "Mode"),
            ["Editor.HighlighterFreehand"] = ("Freehand", "\u624B\u7ED8", "Main levée"),
            ["Editor.HighlighterText"] = ("Text", "\u6587\u672C\u9AD8\u4EAE", "Texte"),
            ["Editor.HighlighterUnderline"] = ("Underline", "\u4E0B\u5212\u7EBF", "Souligné"),
            ["Editor.HighlighterStrikeOut"] = ("Strikeout", "\u5220\u9664\u7EBF", "Barré"),
            ["Editor.HighlighterSquiggly"] = ("Squiggly", "\u6CE2\u6D6A\u7EBF", "Ondulé"),
            ["Editor.HighlighterArea"] = ("Area", "\u533A\u57DF", "Zone"),
            ["Editor.Pressure"] = ("Pressure", "\u538B\u611F", "Pression"),
            ["Editor.InkSimulation"] = ("Ink simulation", "\u58A8\u6C34\u6A21\u62DF", "Simulation d'encre"),
            ["Editor.ShapeRecognition"] = ("Shape recognition", "\u5F62\u72B6\u8BC6\u522B", "Reconnaissance des formes"),
            ["Editor.SmoothingHeader"] = ("Smoothing", "\u5E73\u6ED1", "Lissage"),
            ["Editor.SmoothingOff"] = ("Off", "\u5173", "Désactivé"),
            ["Editor.SmoothingLow"] = ("Low", "\u4F4E", "Faible"),
            ["Editor.SmoothingMid"] = ("Medium", "\u4E2D", "Moyen"),
            ["Editor.SmoothingHigh"] = ("High", "\u9AD8", "Élevé"),
            ["Editor.SmallerText"] = ("Smaller text", "\u51CF\u5C0F\u6587\u5B57", "Texte plus petit"),
            ["Editor.BiggerText"] = ("Bigger text", "\u589E\u5927\u6587\u5B57", "Texte plus grand"),
            ["Editor.TextColorTooltip"] = ("Text color", "\u6587\u5B57\u989C\u8272", "Couleur du texte"),
            ["Editor.BoldTooltip"] = ("Bold", "\u7C97\u4F53", "Gras"),
            ["Editor.ItalicTooltip"] = ("Italic", "\u659C\u4F53", "Italique"),
            ["Editor.FontFamilyTooltip"] = ("Font family", "\u5B57\u4F53", "Famille de polices"),
            ["Editor.AlignmentTooltip"] = ("Alignment", "\u5BF9\u9F50\u65B9\u5F0F", "Alignement"),
            ["Editor.AlignmentLeft"] = ("Left", "\u5DE6\u5BF9\u9F50", "Gauche"),
            ["Editor.AlignmentCenter"] = ("Center", "\u5C45\u4E2D\u5BF9\u9F50", "Centrer"),
            ["Editor.AlignmentRight"] = ("Right", "\u53F3\u5BF9\u9F50", "Droite"),
            ["Editor.Recent"] = ("Recent", "\u6700\u8FD1", "Récent"),
            ["Editor.SavedSuccessfully"] = ("Saved successfully", "\u4FDD\u5B58\u6210\u529F", "Enregistré"),
            ["Editor.NoVersionHistory"] = ("No version history available", "\u6682\u65E0\u7248\u672C\u5386\u53F2", "Aucun historique de versions"),
            ["Editor.RestoredVersion"] = ("Restored version from {0}", "\u5DF2\u4ECE {0} \u6062\u590D\u7248\u672C", "Version restaurée du {0}"),
            ["Editor.VersionLoadFailed"] = ("Failed to load version", "\u52A0\u8F7D\u7248\u672C\u5931\u8D25", "Échec du chargement de la version"),
            ["Editor.PngExported"] = ("PNG exported ({0} pages, {1}x)", "PNG \u5DF2\u5BFC\u51FA\uFF08{0} \u9875\uFF0C{1}x\uFF09", "PNG exporté ({0} pages, {1}x)"),
            ["Editor.PngExportFailed"] = ("PNG export failed: {0}", "PNG \u5BFC\u51FA\u5931\u8D25\uFF1A{0}", "Échec de l'export PNG : {0}"),
            ["Editor.SourcePdfReadFailed"] = ("Unable to read the source PDF: {0}", "\u65E0\u6CD5\u8BFB\u53D6\u6E90 PDF\uFF1A{0}", "Impossible de lire le PDF source : {0}"),
            ["Editor.PageRotated"] = ("Page rotated 90°", "\u9875\u9762\u5DF2\u65CB\u8F6C 90°", "Page tournée de 90°"),
            ["Editor.RotateFailed"] = ("Rotate failed: {0}", "\u65CB\u8F6C\u5931\u8D25\uFF1A{0}", "Échec de la rotation : {0}"),
            ["Editor.PreparingPrint"] = ("Preparing print...", "\u6B63\u5728\u51C6\u5907\u6253\u5370...", "Préparation de l'impression..."),
            ["Editor.PrintSent"] = ("Print job sent", "\u6253\u5370\u4EFB\u52A1\u5DF2\u53D1\u9001", "Tâche d'impression envoyée"),
            ["Editor.PrintFailed"] = ("Failed to print PDF: {0}", "\u6253\u5370 PDF \u5931\u8D25\uFF1A{0}", "Échec de l'impression du PDF : {0}"),
            ["Editor.SaveFailed"] = ("Failed to save annotations: {0}", "\u4FDD\u5B58\u6CE8\u91CA\u5931\u8D25\uFF1A{0}", "Échec de l'enregistrement des annotations : {0}"),
            ["Editor.AutoSaveFailed"] = ("Auto-save failed: {0}", "\u81EA\u52A8\u4FDD\u5B58\u5931\u8D25\uFF1A{0}", "Échec de l'enregistrement automatique : {0}"),
            ["Editor.UndoFailed"] = ("Undo failed: {0}", "\u64A4\u9500\u5931\u8D25\uFF1A{0}", "Échec de l'annulation : {0}"),
            ["Editor.RedoFailed"] = ("Redo failed: {0}", "\u91CD\u505A\u5931\u8D25\uFF1A{0}", "Échec du rétablissement : {0}"),
            ["Editor.Searching"] = ("Searching...", "\u641C\u7D22\u4E2D...", "Recherche..."),
            ["Editor.PrintTooltip"] = ("Print...", "\u6253\u5370...", "Imprimer..."),
            ["Editor.VersionHistoryTooltip"] = ("View version history", "\u67E5\u770B\u7248\u672C\u5386\u53F2", "Afficher l'historique des versions"),
            ["Editor.PenOnlyTooltip"] = ("Pen only", "\u4EC5\u7B14\u7ED8\u5236", "Stylet uniquement"),
            ["Editor.RotateTooltip"] = ("Rotate 90°", "\u65CB\u8F6C 90°", "Tourner de 90°"),
            ["Editor.ImmersiveTooltip"] = ("Immersive mode (F11)", "沉浸模式（F11）", "Mode immersif (F11)"),
            ["Editor.StickyNoteTooltip"] = ("Sticky note", "\u4FBF\u7B7E", "Note autocollante"),
            ["Editor.MoveStickyNoteEditor"] = ("Drag to move this note", "\u62D6\u52A8\u4EE5\u79FB\u52A8\u6B64\u4FBF\u7B7E", "Faire glisser pour d\u00E9placer cette note"),
            ["Editor.HiddenInkTooltip"] = ("Hidden ink — click to reveal for 3 seconds", "\u9690\u85CF\u58A8\u8FF9\u2014\u2014\u70B9\u51FB\u663E\u793A 3 \u79D2", "Encre masquée — cliquer pour afficher pendant 3 secondes"),
            ["Editor.RulerTooltip"] = ("Ruler", "\u76F4\u5C3A", "Règle"),
            ["Editor.SaveDocumentTooltip"] = ("Save document", "\u4FDD\u5B58\u6587\u6863", "Enregistrer le document"),
            ["Editor.CurrentPagePng"] = ("Export current page PNG ({0}x)", "\u5BFC\u51FA\u5F53\u524D\u9875 PNG\uFF08{0}x\uFF09", "Exporter la page actuelle en PNG ({0}x)"),
            ["Editor.AllPagesPng"] = ("Export all pages PNG ({0}x)", "\u5BFC\u51FA\u5168\u90E8\u9875 PNG\uFF08{0}x\uFF09", "Exporter toutes les pages en PNG ({0}x)"),
            ["Editor.InsertPdfPage"] = ("Insert pages from PDF", "\u4ECE PDF \u63D2\u5165\u9875\u9762", "Insérer des pages depuis un PDF"),
            ["Editor.InsertImagePage"] = ("Insert page from image", "\u4ECE\u56FE\u7247\u63D2\u5165\u9875\u9762", "Insérer une page depuis une image"),
            ["Editor.RotateCurrentPage"] = ("Rotate current page 90°", "\u65CB\u8F6C\u5F53\u524D\u9875 90°", "Tourner la page actuelle de 90°"),
            ["Editor.SidebarCollapse"] = ("Collapse panel", "\u6536\u8D77\u9762\u677F", "Réduire le panneau"),
            ["Editor.SidebarSelected"] = ("Selected", "\u5DF2\u9009\u62E9", "Sélectionné"),
            ["Editor.ToolbarScroll"] = ("Scroll toolbar to reveal more tools", "\u6EDA\u52A8\u5DE5\u5177\u680F\u67E5\u770B\u66F4\u591A\u5DE5\u5177", "Faire défiler la barre d’outils pour afficher plus d’outils"),
            ["Editor.PagesTab"] = ("Pages", "\u9875\u9762", "Pages"),
            ["Editor.OutlineTab"] = ("Outline", "\u5927\u7EB2", "Plan"),
            ["Editor.BookmarksTab"] = ("Bookmarks", "\u4E66\u7B7E", "Signets"),
            ["Editor.SidebarNoBookmarks"] = ("No bookmarks yet", "\u6682\u65E0\u4E66\u7B7E", "Aucun signet pour le moment"),
            ["Editor.BookmarkCurrentPage"] = ("Bookmark current page", "\u6536\u85CF\u5F53\u524D\u9875", "Ajouter la page actuelle aux signets"),
            ["Home.Context.CopyPath"] = ("Copy path", "\u590D\u5236\u8DEF\u5F84", "Copier le chemin"),
            ["Home.Context.Export"] = ("Export copy", "\u5BFC\u51FA\u526F\u672C", "Exporter une copie"),
            ["Home.Context.Open"] = ("Open", "\u6253\u5F00", "Ouvrir"),
            ["Home.Context.OpenFolder"] = ("Open folder", "\u6253\u5F00\u6240\u5728\u6587\u4EF6\u5939", "Ouvrir le dossier"),
            ["Home.Context.MoveToLibrary"] = ("Move to library root", "\u79FB\u5230\u5E93\u6839\u76EE\u5F55", "D\u00E9placer vers la racine de la biblioth\u00E8que"),
            ["Home.Context.Remove"] = ("Remove from library", "\u4ECE\u5E93\u4E2D\u79FB\u9664", "Retirer de la biblioth\u00E8que"),
            ["Home.Context.RemoveFolder"] = ("Remove folder", "\u79FB\u9664\u6587\u4EF6\u5939", "Supprimer le dossier"),
            ["Home.Context.Rename"] = ("Rename", "\u91CD\u547D\u540D", "Renommer"),
            ["Home.Context.Select"] = ("Select", "\u9009\u62E9", "S\u00E9lection"),
            ["Home.CreateFolderAction"] = ("Create folder", "\u521B\u5EFA\u6587\u4EF6\u5939", "Cr\u00E9er un dossier"),
            ["Home.CreateFolderPrompt"] = ("Enter a name for the new folder:", "\u8F93\u5165\u65B0\u6587\u4EF6\u5939\u540D\u79F0\uFF1A", "Saisissez un nom pour le nouveau dossier :"),
            ["Home.CreateFolderTitle"] = ("Create folder", "\u521B\u5EFA\u6587\u4EF6\u5939", "Cr\u00E9er un dossier"),
            ["Home.CreateNotebookAction"] = ("Create notebook", "\u521B\u5EFA\u7B14\u8BB0\u672C", "Cr\u00E9er le carnet"),
            ["Home.CreateNotebookBrowseFolder"] = ("Choose folder", "\u9009\u62E9\u6587\u4EF6\u5939", "Choisir le dossier"),
            ["Home.CreateNotebookDialogSubtitle"] = ("Choose a page style and where to save the new notebook.", "\u9009\u62E9\u9875\u9762\u6837\u5F0F\u548C\u65B0\u7B14\u8BB0\u672C\u7684\u4FDD\u5B58\u4F4D\u7F6E\u3002", "Choisissez un style de page et l'emplacement du nouveau carnet."),
            ["Home.CreateNotebookDialogTitle"] = ("Create notebook", "\u521B\u5EFA\u7B14\u8BB0\u672C", "Cr\u00E9er un carnet"),
            ["Home.CreateNotebookFailed"] = ("Failed to create notebook: {0}", "\u65E0\u6CD5\u521B\u5EFA\u7B14\u8BB0\u672C\uFF1A{0}", "\u00C9chec de la cr\u00E9ation du carnet : {0}"),
            ["Home.CreateNotebookPathLabel"] = ("Save to", "\u4FDD\u5B58\u5230", "Enregistrer dans"),
            ["Home.ErrorAccessDenied"] = ("File not found or access denied.", "\u627E\u4E0D\u5230\u6587\u4EF6\u6216\u65E0\u6743\u8BBF\u95EE\u3002", "Fichier introuvable ou acc\u00E8s refus\u00E9."),
            ["Home.ErrorFileNotFound"] = ("File not found.", "\u627E\u4E0D\u5230\u6587\u4EF6\u3002", "Fichier introuvable."),
            ["Home.ErrorUnsupportedType"] = ("Unsupported file type.", "\u4E0D\u652F\u6301\u7684\u6587\u4EF6\u7C7B\u578B\u3002", "Type de fichier non pris en charge."),
            ["Home.ExportFailed"] = ("Failed to export: {0}", "\u5BFC\u51FA\u5931\u8D25\uFF1A{0}", "\u00C9chec de l'export : {0}"),
            ["Home.ExportSucceeded"] = ("Exported copy saved.", "\u5DF2\u4FDD\u5B58\u5BFC\u51FA\u526F\u672C\u3002", "Copie export\u00E9e enregistr\u00E9e."),
            ["Home.ExportTitle"] = ("Export PDF Copy", "\u5BFC\u51FA PDF \u526F\u672C", "Exporter une copie PDF"),
            ["Home.FolderCreated"] = ("Folder \"{0}\" created.", "\u5DF2\u521B\u5EFA\u6587\u4EF6\u5939\u201C{0}\u201D\u3002", "Dossier \"{0}\" cr\u00E9\u00E9."),
            ["Home.FolderSubtitle"] = ("Organize the files inside {0}.", "\u7BA1\u7406 {0} \u4E2D\u7684\u6587\u4EF6\u3002", "Organisez les fichiers dans {0}."),
            ["Home.Info.Pages"] = ("{0} pages", "{0} \u9875", "{0} pages"),
            ["Home.Info.Items"] = ("{0} items", "{0} \u9879", "{0} \u00E9l\u00E9ments"),
            ["Home.Info.Notebook"] = ("Notebook", "\u7B14\u8BB0\u672C", "Carnet"),
            ["Home.LibraryRoot"] = ("Library", "\u5E93", "Biblioth\u00E8que"),
            ["Home.Menu.CreateFolder"] = ("Create folder", "\u521B\u5EFA\u6587\u4EF6\u5939", "Cr\u00E9er un dossier"),
            ["Home.Menu.CreateNotebook"] = ("Create empty notebook", "\u521B\u5EFA\u7A7A\u767D\u7B14\u8BB0\u672C", "Cr\u00E9er un carnet vide"),
            ["Home.Menu.OpenFile"] = ("Open file", "\u6253\u5F00\u6587\u4EF6", "Ouvrir un fichier"),
            ["Home.MovedToFolder"] = ("Moved to {0}.", "\u5DF2\u79FB\u52A8\u5230 {0}\u3002", "D\u00E9plac\u00E9 vers {0}."),
            ["Home.NavigateUp"] = ("Back", "\u8FD4\u56DE", "Retour"),
            ["Home.NewNotebookName"] = ("Untitled Notebook", "\u672A\u547D\u540D\u7B14\u8BB0\u672C", "Carnet sans titre"),
            ["Home.NotebookSaved"] = ("Notebook saved.", "\u7B14\u8BB0\u672C\u5DF2\u4FDD\u5B58\u3002", "Carnet enregistr\u00E9."),
            ["Home.OpenPdfTitle"] = ("Open PDF File", "\u6253\u5F00 PDF \u6587\u4EF6", "Ouvrir un fichier PDF"),
            ["Home.PdfFilter"] = ("PDF Files (*.pdf)|*.pdf", "PDF \u6587\u4EF6 (*.pdf)|*.pdf", "Fichiers PDF (*.pdf)|*.pdf"),
            ["Home.RenameAction"] = ("Rename", "\u91CD\u547D\u540D", "Renommer"),
            ["Home.RenameFailed"] = ("Failed to rename: {0}", "\u91CD\u547D\u540D\u5931\u8D25\uFF1A{0}", "\u00C9chec du renommage : {0}"),
            ["Home.RenameFolderPrompt"] = ("Enter a new name for this folder:", "\u8F93\u5165\u6587\u4EF6\u5939\u7684\u65B0\u540D\u79F0\uFF1A", "Saisissez un nouveau nom pour ce dossier :"),
            ["Home.RenameFolderTitle"] = ("Rename Folder", "\u91CD\u547D\u540D\u6587\u4EF6\u5939", "Renommer le dossier"),
            ["Home.RenamePrompt"] = ("Enter a new name for this file:", "\u8F93\u5165\u6587\u4EF6\u7684\u65B0\u540D\u79F0\uFF1A", "Saisissez un nouveau nom pour ce fichier :"),
            ["Home.RenameTitle"] = ("Rename File", "\u91CD\u547D\u540D\u6587\u4EF6", "Renommer le fichier"),
            ["Home.SaveNotebookTitle"] = ("Save Notebook", "\u4FDD\u5B58\u7B14\u8BB0\u672C", "Enregistrer le carnet"),
            ["Home.Selection.Clear"] = ("Clear selection", "\u6E05\u9664\u9009\u62E9", "Effacer la s\u00E9lection"),
            ["Home.Selection.Count"] = ("{0} selected", "\u5DF2\u9009\u62E9 {0} \u4E2A", "{0} s\u00E9lectionn\u00E9(s)"),
            ["Home.Selection.Done"] = ("Done", "\u5B8C\u6210", "Termin\u00E9"),
            ["Home.Selection.Hint"] = ("Selected files will be removed from the library only. The original files stay on disk.", "\u9009\u4E2D\u7684\u6587\u4EF6\u53EA\u4F1A\u4ECE\u5E93\u4E2D\u79FB\u9664\uFF0C\u539F\u59CB\u6587\u4EF6\u4ECD\u4FDD\u7559\u5728\u78C1\u76D8\u4E0A\u3002", "Les fichiers s\u00E9lectionn\u00E9s seront retir\u00E9s de la biblioth\u00E8que uniquement. Les originaux restent sur le disque."),
            ["Home.Selection.None"] = ("No files selected", "未选择文件", "Aucun fichier sélectionné"),
            ["Home.Selection.Move"] = ("Move to folder", "移动到文件夹", "Déplacer vers un dossier"),
            ["Home.Selection.Remove"] = ("Remove from library", "从库中移除", "Retirer de la bibliothèque"),
            ["Home.Selection.RemovedCount"] = ("Removed {0} file(s) from the library.", "\u5DF2\u4ECE\u5E93\u4E2D\u79FB\u9664 {0} \u4E2A\u6587\u4EF6\u3002", "{0} fichier(s) retir\u00E9(s) de la biblioth\u00E8que."),
            ["Home.Selection.SelectAll"] = ("Select all", "\u5168\u9009", "Tout s\u00E9lectionner"),
            ["Home.Subtitle"] = ("Open a PDF, create a notebook, or organize your library.", "\u6253\u5F00 PDF\u3001\u521B\u5EFA\u7B14\u8BB0\u672C\uFF0C\u6216\u6574\u7406\u4F60\u7684\u5E93\u3002", "Ouvrez un PDF, cr\u00E9ez un carnet ou organisez votre biblioth\u00E8que."),
            ["Home.Title"] = ("Library", "\u5E93", "Biblioth\u00E8que"),
            ["Main.About"] = ("About", "\u5173\u4E8E", "\u00C0 propos"),
            ["Main.AboutMessage"] = ("OpenNotes\nA paper-like PDF annotation workspace for Windows", "OpenNotes\n\u4E00\u4E2A\u7EB8\u5F20\u822C\u7684 Windows PDF \u6279\u6CE8\u5DE5\u4F5C\u53F0", "OpenNotes\nUn espace d'annotation PDF sur Windows, comme une feuille de papier"),
            ["Main.AboutTitle"] = ("About", "\u5173\u4E8E", "\u00C0 propos"),
            ["Main.CloseTabTooltip"] = ("Close tab", "\u5173\u95ED\u9009\u9879\u5361", "Fermer l'onglet"),
            ["Main.FileAutoSaved"] = ("File auto-saved", "\u6587\u4EF6\u5DF2\u81EA\u52A8\u4FDD\u5B58", "Fichier enregistr\u00E9 automatiquement"),
            ["Main.HomeTabTitle"] = ("Home", "\u4E3B\u9875", "Accueil"),
            ["Main.NewTabTooltip"] = ("New tab (Ctrl+T)", "\u65B0\u5EFA\u9009\u9879\u5361 (Ctrl+T)", "Nouvel onglet (Ctrl+T)"),
            ["Main.SearchPlaceholder"] = ("Search library", "\u641C\u7D22\u5E93", "Rechercher dans la biblioth\u00E8que"),
            ["Main.Select"] = ("Select", "\u9009\u62E9", "S\u00E9lection"),
            ["Main.SelectionDisabled"] = ("Select mode disabled", "\u9009\u62E9\u6A21\u5F0F\u5DF2\u5173\u95ED", "Mode s\u00E9lection d\u00E9sactiv\u00E9"),
            ["Main.SelectionEnabled"] = ("Select mode enabled", "\u9009\u62E9\u6A21\u5F0F\u5DF2\u542F\u7528", "Mode s\u00E9lection activ\u00E9"),
            ["Main.Settings"] = ("Settings", "\u8BBE\u7F6E", "Param\u00E8tres"),
            ["Main.SettingsSaved"] = ("Settings saved", "\u8BBE\u7F6E\u5DF2\u4FDD\u5B58", "Param\u00E8tres enregistr\u00E9s"),
            ["Main.SortByDate"] = ("Sort by date", "\u6309\u65E5\u671F\u6392\u5E8F", "Trier par date"),
            ["Main.SortByName"] = ("Sort by name", "\u6309\u540D\u79F0\u6392\u5E8F", "Trier par nom"),
            ["Product.Description"] = ("A paper-like PDF annotation workspace for Windows.", "\u4E00\u4E2A\u7EB8\u5F20\u822C\u7684 Windows PDF \u6279\u6CE8\u5DE5\u4F5C\u53F0\u3002", "Un espace d’annotation PDF sur Windows, comme une feuille de papier."),
            ["Settings.LanguageHint"] = ("Choose the interface language for OpenNotes. Changes preview immediately.", "\u9009\u62E9 OpenNotes \u7684\u754C\u9762\u8BED\u8A00\u3002\u66F4\u6539\u4F1A\u7ACB\u5373\u9884\u89C8\u3002", "Choisissez la langue de l'interface d'OpenNotes. Les changements sont pr\u00E9visualis\u00E9s imm\u00E9diatement."),
            ["Settings.LanguageLabel"] = ("Display language", "\u663E\u793A\u8BED\u8A00", "Langue d'affichage"),
            ["Settings.Subtitle"] = ("Customize utilities and language.", "\u8C03\u6574\u5DE5\u5177\u529F\u80FD\u4E0E\u8BED\u8A00\u3002", "Personnalisez les utilitaires et la langue."),
            ["Settings.Title"] = ("Settings", "\u8BBE\u7F6E", "Param\u00E8tres"),
            ["Settings.UtilityHint"] = ("On the Home page, Select mode enables multi-select. In document tabs, it switches back to text selection.", "\u5728\u4E3B\u9875\u4E2D\uFF0C\u9009\u62E9\u6A21\u5F0F\u53EF\u542F\u7528\u591A\u9009\u3002\u5728\u6587\u6863\u9009\u9879\u5361\u4E2D\uFF0C\u5B83\u4F1A\u5207\u56DE\u6587\u672C\u9009\u62E9\u3002", "Sur l'accueil, le mode S\u00E9lection active la s\u00E9lection multiple. Dans les onglets de document, il r\u00E9active la s\u00E9lection de texte."),
            ["Settings.UtilityLabel"] = ("Utility modes", "\u5DE5\u5177\u6A21\u5F0F", "Modes utilitaires"),
            ["Settings.AutoSaveInterval"] = ("Auto-save interval", "\u81EA\u52A8\u4FDD\u5B58\u95F4\u9694", "Intervalle d'enregistrement automatique"),
            ["Settings.Pressure"] = ("Pressure", "\u538B\u611F", "Pression"),
            ["Settings.Enabled"] = ("Enabled", "\u542F\u7528", "Activé"),
            ["Settings.PenOnly"] = ("Pen-only drawing", "\u4EC5\u7B14\u7ED8\u5236", "Dessin au stylet uniquement"),
            ["Settings.Smoothing"] = ("Smoothing", "\u7B14\u8FF9\u5E73\u6ED1", "Lissage"),
            ["Settings.DefaultPenColor"] = ("Default pen color", "\u9ED8\u8BA4\u7B14\u989C\u8272", "Couleur de stylet par défaut"),
            ["Settings.DefaultPenSize"] = ("Default pen size", "\u9ED8\u8BA4\u7B14\u7C97\u7EC6", "Épaisseur de stylet par défaut"),
            ["Settings.Performance"] = ("Performance", "\u6027\u80FD", "Performances"),
            ["Settings.PerformanceBatterySaver"] = ("Battery saver", "\u8282\u80FD", "Économie d’énergie"),
            ["Settings.PerformanceBalanced"] = ("Balanced (recommended)", "\u5747\u8861\uFF08\u63A8\u8350\uFF09", "Équilibré (recommandé)"),
            ["Settings.PerformanceBestQuality"] = ("Best quality", "\u6700\u4F73\u8D28\u91CF", "Qualité optimale"),
            ["Settings.Theme"] = ("Theme", "\u4E3B\u9898", "Thème"),
            ["Settings.ThemeLight"] = ("Light", "\u6D45\u8272", "Clair"),
            ["Settings.ThemeDark"] = ("Dark", "\u6DF1\u8272", "Sombre"),
            ["Settings.ThemeSystem"] = ("System", "\u8DDF\u968F\u7CFB\u7EDF", "Système"),
            ["Settings.ThemeHighContrast"] = ("High contrast", "\u9AD8\u5BF9\u6BD4\u5EA6", "Contraste élevé"),
            ["Settings.WorkspaceBackdrop"] = ("Workspace backdrop", "\u5DE5\u4F5C\u533A\u80CC\u666F", "Fond de l’espace de travail"),
            ["Settings.WorkspaceBackdropHint"] = ("Changes the desk around PDF pages, never the PDF itself.", "\u4EC5\u6539\u53D8 PDF \u9875\u9762\u5468\u56F4\u7684\u5DE5\u4F5C\u533A\uFF0C\u4E0D\u4F1A\u6539\u53D8 PDF \u5185\u5BB9\u3002", "Modifie le bureau autour des pages PDF, jamais le PDF lui-même."),
            ["Settings.WorkspaceBackdropNeutral"] = ("White", "\u767D\u8272", "Blanc"),
            ["Settings.WorkspaceBackdropPaper"] = ("Paper", "\u7EB8\u5F20", "Papier"),
            ["Settings.WorkspaceBackdropMist"] = ("Mist", "\u96FE\u84DD", "Brume"),
            ["Settings.WorkspaceBackdropWarm"] = ("Warm paper", "\u6696\u7EB8", "Papier chaud"),
            ["Settings.WorkspaceBackdropSlate"] = ("Slate", "\u677F\u5CA9", "Ardoise"),
            ["Settings.WorkspaceBackdropMidnight"] = ("Midnight", "\u5348\u591C", "Minuit")
        };

        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

        public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        public static event EventHandler LanguageChanged;

        public static IReadOnlyList<LanguageOption> GetLanguageOptions() => LanguageOptions;

        public static IReadOnlyDictionary<string, (string English, string Chinese, string French)> GetCatalog() => Strings;

        public static void ApplyLanguage(AppLanguage language)
        {
            if (CurrentLanguage == language)
                return;

            CurrentLanguage = language;
            CurrentCulture = language switch
            {
                AppLanguage.Chinese => CultureInfo.GetCultureInfo("zh-CN"),
                AppLanguage.French => CultureInfo.GetCultureInfo("fr-FR"),
                _ => CultureInfo.GetCultureInfo("en-US")
            };

            Thread.CurrentThread.CurrentCulture = CurrentCulture;
            Thread.CurrentThread.CurrentUICulture = CurrentCulture;
            CultureInfo.DefaultThreadCurrentCulture = CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Get(string key)
        {
            return GetForLanguage(key, CurrentLanguage);
        }

        public static string GetForLanguage(string key, AppLanguage language)
        {
            if (!Strings.TryGetValue(key, out var value))
                throw new KeyNotFoundException($"Missing localization key: {key}");

            return language switch
            {
                AppLanguage.Chinese => value.Chinese,
                AppLanguage.French => value.French,
                _ => value.English
            };
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CurrentCulture, Get(key), args);
        }

        public static string FormatForLanguage(string key, AppLanguage language, params object[] args)
        {
            CultureInfo culture = language switch
            {
                AppLanguage.Chinese => CultureInfo.GetCultureInfo("zh-CN"),
                AppLanguage.French => CultureInfo.GetCultureInfo("fr-FR"),
                _ => CultureInfo.GetCultureInfo("en-US")
            };
            return string.Format(culture, GetForLanguage(key, language), args);
        }
    }
}
