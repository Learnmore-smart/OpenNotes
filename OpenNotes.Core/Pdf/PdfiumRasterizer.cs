using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Caelum.Pdf
{
    /// <summary>
    /// Thrown when pdfium fails to load a document. The numeric code mirrors
    /// pdfium's <c>FPDF_GetLastError</c> values (2=file, 3=format,
    /// 4=password, 5=security, 6=page).
    /// </summary>
    public sealed class PdfiumException : Exception
    {
        public PdfiumException(uint errorCode)
            : base($"pdfium document load failed (FPDF error {errorCode})")
        {
            ErrorCode = errorCode;
        }

        public PdfiumException(string message)
            : base(message)
        {
        }

        public uint ErrorCode { get; }
    }

    /// <summary>
    /// Default <see cref="IPdfRasterizerFactory"/> backed by pdfium.dll.
    /// </summary>
    public sealed class PdfiumRasterizerFactory : IPdfRasterizerFactory
    {
        public static readonly PdfiumRasterizerFactory Shared = new PdfiumRasterizerFactory();

        public IPdfRasterizer LoadFromStream(Stream stream) => new PdfiumRasterizer(stream);

        public IPdfRasterizer LoadFromFile(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            // Mirror PdfiumViewer.PdfDocument.Load(path): it always opens a
            // FileStream and feeds the stream loader, keeping the handle for
            // the document's lifetime.
            return new PdfiumRasterizer(File.OpenRead(path));
        }
    }

    /// <summary>
    /// <see cref="IPdfRasterizer"/> implemented by direct P/Invoke against
    /// pdfium.dll (no System.Drawing / Windows.Forms dependency, so it can
    /// live in the UI-free Core assembly and serve WPF + WinUI equally).
    /// <para>
    /// Every native call is serialized through <see cref="PdfiumNative.SyncRoot"/> —
    /// the same interned-string monitor PdfiumViewer uses — so this class and
    /// the legacy <c>PdfiumViewer.PdfDocument</c> can never enter pdfium
    /// concurrently (pdfium itself is not thread-safe).
    /// </para>
    /// <para>
    /// The document lifetime contract mirrors PdfiumViewer's
    /// <c>PdfFile</c>: the source stream is read lazily through
    /// FPDF_LoadCustomDocument and must outlive the document; it is disposed
    /// here when the rasterizer is disposed.
    /// </para>
    /// </summary>
    public sealed class PdfiumRasterizer : IPdfRasterizer
    {
        private static readonly Encoding FpdfEncoding = new UnicodeEncoding(false, false, false);

        private IntPtr _document;
        private IntPtr _form;
        private bool _disposed;
        private PdfiumNative.FpdfFormFillInfo _formInfo;
        private GCHandle _formInfoHandle;
        private GCHandle _streamHandle;
        private Stream _stream;
        private readonly IReadOnlyList<PdfPageSize> _pageSizes;

        public PdfiumRasterizer(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            PdfiumNative.EnsureLoaded();

            _stream = stream;
            _streamHandle = GCHandle.Alloc(stream);

            try
            {
                _document = PdfiumNative.LoadCustomDocument(stream, _streamHandle);
                if (_document == IntPtr.Zero)
                    throw new PdfiumException(PdfiumNative.FPDF_GetLastError());
            }
            catch
            {
                if (_streamHandle.IsAllocated)
                    _streamHandle.Free();
                _stream = null;
                stream.Dispose();
                throw;
            }

            try
            {
                InitializeFormEnvironment();
            }
            catch
            {
                if (_form != IntPtr.Zero)
                {
                    PdfiumNative.FPDFDOC_ExitFormFillEnvironment(_form);
                    _form = IntPtr.Zero;
                }
                if (_formInfoHandle.IsAllocated)
                    _formInfoHandle.Free();
                PdfiumNative.FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
                if (_streamHandle.IsAllocated)
                    _streamHandle.Free();
                _stream = null;
                stream.Dispose();
                throw;
            }

            _pageSizes = LoadPageSizes();
        }

        /// <summary>
        /// Second-stage initialization matching PdfiumViewer's LoadDocument:
        /// form-fill environment (version 1 or 2), highlight color/alpha, then
        /// the document-level JS/open actions.
        /// </summary>
        private void InitializeFormEnvironment()
        {
            PdfiumNative.FPDF_GetDocPermissions(_document);

            // FPDF_FORMFILLINFO pinned for the form env's lifetime. All 15 v1
            // callback slots carry real (mostly no-op) delegates — mirroring
            // PdfiumViewer, which never hands pdfium a null function pointer.
            // pdfium may call e.g. FFI_GetRotation/FFI_GetPage while running
            // document or page actions, so nulls here would be a latent crash
            // on action-bearing documents that the old backend tolerated.
            _formInfo = PdfiumNative.CreateFormFillInfo();
            _formInfoHandle = GCHandle.Alloc(_formInfo, GCHandleType.Pinned);
            IntPtr formInfoPtr = _formInfoHandle.AddrOfPinnedObject();

            for (int version = 1; version <= 2; version++)
            {
                Marshal.WriteInt32(formInfoPtr, version);
                _form = PdfiumNative.FPDFDOC_InitFormFillEnvironment(_document, formInfoPtr);
                if (_form != IntPtr.Zero)
                    break;
            }

            PdfiumNative.FPDF_SetFormFieldHighlightColor(_form, 0, 0xFFE4DD);
            PdfiumNative.FPDF_SetFormFieldHighlightAlpha(_form, 100);

            PdfiumNative.FORM_DoDocumentJSAction(_form);
            PdfiumNative.FORM_DoDocumentOpenAction(_form);
        }

        private IReadOnlyList<PdfPageSize> LoadPageSizes()
        {
            int pageCount = PdfiumNative.FPDF_GetPageCount(_document);
            var result = new List<PdfPageSize>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                PdfiumNative.FPDF_GetPageSizeByIndex(_document, i, out double width, out double height);
                result.Add(new PdfPageSize((float)width, (float)height));
            }
            return result;
        }

        public int PageCount => _pageSizes?.Count ?? 0;

        public IReadOnlyList<PdfPageSize> PageSizes => _pageSizes ?? (IReadOnlyList<PdfPageSize>)Array.Empty<PdfPageSize>();

        public PdfPageBitmap RenderPageBgra(int pageIndex, int pixelWidth, int pixelHeight, bool renderAnnotations = true)
        {
            ThrowIfDisposed();
            if (pixelWidth <= 0 || pixelHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Render size must be positive.");

            var pixels = new byte[checked(pixelWidth * 4 * pixelHeight)];
            GCHandle pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                bitmapHandle = PdfiumNative.FPDFBitmap_CreateEx(
                    pixelWidth, pixelHeight, PdfiumNative.FPDFBitmapFormatBgra,
                    pixelsHandle.AddrOfPinnedObject(), pixelWidth * 4);

                // Opaque white background — same as the PdfiumViewer path when
                // PdfRenderFlags.Transparent is not set.
                PdfiumNative.FPDFBitmap_FillRect(bitmapHandle, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFF);

                using (var pageData = new PageData(_document, _form, pageIndex))
                {
                    // PdfRenderFlags.Annotations semantics from PdfiumViewer:
                    // FPDF_ANNOT is stripped for the base pass and annotations
                    // are drawn through the form-fill environment instead.
                    // renderAnnotations == false mirrors the old
                    // (PdfRenderFlags)0 call — base pass only, no FFLDraw.
                    PdfiumNative.FPDF_RenderPageBitmap(
                        bitmapHandle, pageData.Page, 0, 0, pixelWidth, pixelHeight, 0, 0);
                    if (renderAnnotations)
                    {
                        PdfiumNative.FPDF_FFLDraw(
                            _form, bitmapHandle, pageData.Page, 0, 0, pixelWidth, pixelHeight, 0, 0);
                    }
                }
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                    PdfiumNative.FPDFBitmap_Destroy(bitmapHandle);
                if (pixelsHandle.IsAllocated)
                    pixelsHandle.Free();
            }

            return new PdfPageBitmap
            {
                Width = pixelWidth,
                Height = pixelHeight,
                Stride = pixelWidth * 4,
                Bgra = pixels
            };
        }

        public string GetPageText(int pageIndex)
        {
            ThrowIfDisposed();
            using (var pageData = new PageData(_document, _form, pageIndex))
            {
                int length = PdfiumNative.FPDFText_CountChars(pageData.TextPage);
                return GetPageText(pageData, 0, length);
            }
        }

        private string GetPageText(PageData pageData, int offset, int length)
        {
            var result = new byte[checked((length + 1) * 2)];
            PdfiumNative.FPDFText_GetText(pageData.TextPage, offset, length, result);
            return FpdfEncoding.GetString(result, 0, length * 2);
        }

        public IReadOnlyList<PdfRectF> GetTextBounds(int pageIndex, int offset, int length)
        {
            ThrowIfDisposed();
            using (var pageData = new PageData(_document, _form, pageIndex))
            {
                var result = new List<PdfRectF>();
                PdfRectF? lastBounds = null;

                for (int i = 0; i < length; i++)
                {
                    var bounds = GetCharBounds(pageData.TextPage, offset + i);
                    if (bounds.Width == 0 || bounds.Height == 0)
                        continue;

                    // Same horizontal-run merge as PdfiumViewer: adjacent
                    // character boxes on the same baseline collapse into one
                    // rectangle (top = max, bottom = min).
                    if (lastBounds.HasValue &&
                        AreClose(lastBounds.Value.Right, bounds.Left) &&
                        AreClose(lastBounds.Value.Top, bounds.Top) &&
                        AreClose(lastBounds.Value.Bottom, bounds.Bottom))
                    {
                        float top = Math.Max(lastBounds.Value.Top, bounds.Top);
                        float bottom = Math.Min(lastBounds.Value.Bottom, bounds.Bottom);

                        lastBounds = new PdfRectF(
                            lastBounds.Value.Left,
                            top,
                            bounds.Right - lastBounds.Value.Left,
                            bottom - top);

                        result[result.Count - 1] = lastBounds.Value;
                    }
                    else
                    {
                        lastBounds = bounds;
                        result.Add(bounds);
                    }
                }

                return result;
            }
        }

        private static bool AreClose(float p1, float p2) => Math.Abs(p1 - p2) < 4f;

        private static PdfRectF GetCharBounds(IntPtr textPage, int index)
        {
            PdfiumNative.FPDFText_GetCharBox(
                textPage, index,
                out double left, out double right, out double bottom, out double top);

            return new PdfRectF(
                (float)left,
                (float)top,
                (float)(right - left),
                (float)(bottom - top));
        }

        public PdfRectI RectangleFromPdf(int pageIndex, PdfRectF rect)
        {
            ThrowIfDisposed();
            using (var pageData = new PageData(_document, _form, pageIndex))
            {
                PdfiumNative.FPDF_PageToDevice(
                    pageData.Page, 0, 0,
                    (int)pageData.Width, (int)pageData.Height, 0,
                    rect.Left, rect.Top,
                    out int deviceX1, out int deviceY1);

                PdfiumNative.FPDF_PageToDevice(
                    pageData.Page, 0, 0,
                    (int)pageData.Width, (int)pageData.Height, 0,
                    rect.Right, rect.Bottom,
                    out int deviceX2, out int deviceY2);

                return new PdfRectI(deviceX1, deviceY1, deviceX2 - deviceX1, deviceY2 - deviceY1);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            // Order matches PdfiumViewer.PdfFile.Dispose.
            if (_form != IntPtr.Zero)
            {
                PdfiumNative.FORM_DoDocumentAAction(_form, PdfiumNative.FpdfDocAAction.WC);
                PdfiumNative.FPDFDOC_ExitFormFillEnvironment(_form);
                _form = IntPtr.Zero;
            }

            if (_document != IntPtr.Zero)
            {
                PdfiumNative.FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
            }

            if (_formInfoHandle.IsAllocated)
                _formInfoHandle.Free();

            if (_streamHandle.IsAllocated)
                _streamHandle.Free();

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Open page + text page + form-fill page actions, matching
        /// PdfiumViewer's PageData scope (load → AfterLoadPage → OPEN action;
        /// close → CLOSE action → BeforeClosePage → close text → close page).
        /// </summary>
        private sealed class PageData : IDisposable
        {
            private readonly IntPtr _form;
            private bool _disposed;

            public IntPtr Page { get; }
            public IntPtr TextPage { get; }
            public double Width { get; }
            public double Height { get; }

            public PageData(IntPtr document, IntPtr form, int pageIndex)
            {
                _form = form;

                Page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
                TextPage = PdfiumNative.FPDFText_LoadPage(Page);
                PdfiumNative.FORM_OnAfterLoadPage(Page, form);
                PdfiumNative.FORM_DoPageAAction(Page, form, PdfiumNative.FpdfPageAAction.Open);

                Width = PdfiumNative.FPDF_GetPageWidth(Page);
                Height = PdfiumNative.FPDF_GetPageHeight(Page);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                PdfiumNative.FORM_DoPageAAction(Page, _form, PdfiumNative.FpdfPageAAction.Close);
                PdfiumNative.FORM_OnBeforeClosePage(Page, _form);
                PdfiumNative.FPDFText_ClosePage(TextPage);
                PdfiumNative.FPDF_ClosePage(Page);

                _disposed = true;
            }
        }
    }

    /// <summary>
    /// P/Invoke layer for pdfium.dll. Every entry point serializes through
    /// <see cref="SyncRoot"/> — the identical interned string PdfiumViewer
    /// locks on — so the two backends can never drive pdfium concurrently.
    /// </summary>
    internal static class PdfiumNative
    {
        // MUST stay the same interned string as PdfiumViewer's LockString so
        // both backends share one process-wide pdfium monitor.
        private static readonly string LockString = string.Intern("e362349b-001d-4cb2-bf55-a71606a3e36f");
        internal static object SyncRoot => LockString;

        private const string PdfiumDll = "pdfium.dll";
        private static int _libraryLoaded;

        public const int FPDFBitmapFormatBgra = 4;

        internal enum FpdfDocAAction
        {
            WC = 0x10,
            WS = 0x11,
            DS = 0x12,
            WP = 0x13,
            DP = 0x14
        }

        internal enum FpdfPageAAction
        {
            Open = 0,
            Close = 1
        }

        /// <summary>
        /// Preloads pdfium.dll from the app directory's x64/x86 subfolder (same
        /// resolution order as PdfiumViewer's NativeMethods static ctor) and
        /// bumps the library refcount once.
        /// </summary>
        internal static void EnsureLoaded()
        {
            if (_libraryLoaded != 0)
                return;

            lock (SyncRoot)
            {
                if (_libraryLoaded != 0)
                    return;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!TryLoadNativeLibrary(AppDomain.CurrentDomain.RelativeSearchPath))
                        TryLoadNativeLibrary(Path.GetDirectoryName(typeof(PdfiumNative).Assembly.Location));
                }

                FPDF_AddRef();
                _libraryLoaded = 1;
            }
        }

        private static bool TryLoadNativeLibrary(string path)
        {
            if (path == null)
                return false;

            path = Path.Combine(path, IntPtr.Size == 4 ? "x86" : "x64");
            path = Path.Combine(path, PdfiumDll);

            return File.Exists(path) && LoadLibraryW(path) != IntPtr.Zero;
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        internal static IntPtr LoadCustomDocument(Stream input, GCHandle streamHandle)
        {
            var access = new FPDF_FILEACCESS
            {
                m_FileLen = (uint)input.Length,
                m_GetBlock = Marshal.GetFunctionPointerForDelegate(GetBlockDelegate),
                m_Param = GCHandle.ToIntPtr(streamHandle)
            };

            lock (SyncRoot)
                return Imports.FPDF_LoadCustomDocument(in access, null);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FpdfGetBlockDelegate(IntPtr param, uint position, IntPtr buffer, uint size);

        private static readonly FpdfGetBlockDelegate GetBlockDelegate = FpdfGetBlock;

        private static int FpdfGetBlock(IntPtr param, uint position, IntPtr buffer, uint size)
        {
            try
            {
                if (!(GCHandle.FromIntPtr(param).Target is Stream stream))
                    return 0;

                byte[] managedBuffer = new byte[size];
                stream.Position = position;
                int read = stream.Read(managedBuffer, 0, (int)size);
                if (read != size)
                    return 0;

                Marshal.Copy(managedBuffer, 0, buffer, (int)size);
                return 1;
            }
            catch
            {
                // Never let a managed exception escape into pdfium.
                return 0;
            }
        }

        #region Pdfium entry points (each serialized under SyncRoot)

        internal static void FPDF_AddRef()
        {
            lock (SyncRoot)
                Imports.FPDF_AddRef();
        }

        internal static void FPDF_CloseDocument(IntPtr document)
        {
            lock (SyncRoot)
                Imports.FPDF_CloseDocument(document);
        }

        internal static int FPDF_GetPageCount(IntPtr document)
        {
            lock (SyncRoot)
                return Imports.FPDF_GetPageCount(document);
        }

        internal static uint FPDF_GetDocPermissions(IntPtr document)
        {
            lock (SyncRoot)
                return Imports.FPDF_GetDocPermissions(document);
        }

        internal static IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr document, IntPtr formInfo)
        {
            lock (SyncRoot)
                return Imports.FPDFDOC_InitFormFillEnvironment(document, formInfo);
        }

        internal static void FPDF_SetFormFieldHighlightColor(IntPtr hHandle, int fieldType, uint color)
        {
            lock (SyncRoot)
                Imports.FPDF_SetFormFieldHighlightColor(hHandle, fieldType, color);
        }

        internal static void FPDF_SetFormFieldHighlightAlpha(IntPtr hHandle, byte alpha)
        {
            lock (SyncRoot)
                Imports.FPDF_SetFormFieldHighlightAlpha(hHandle, alpha);
        }

        internal static void FORM_DoDocumentJSAction(IntPtr hHandle)
        {
            lock (SyncRoot)
                Imports.FORM_DoDocumentJSAction(hHandle);
        }

        internal static void FORM_DoDocumentOpenAction(IntPtr hHandle)
        {
            lock (SyncRoot)
                Imports.FORM_DoDocumentOpenAction(hHandle);
        }

        internal static void FPDFDOC_ExitFormFillEnvironment(IntPtr hHandle)
        {
            lock (SyncRoot)
                Imports.FPDFDOC_ExitFormFillEnvironment(hHandle);
        }

        internal static void FORM_DoDocumentAAction(IntPtr hHandle, FpdfDocAAction aaType)
        {
            lock (SyncRoot)
                Imports.FORM_DoDocumentAAction(hHandle, (int)aaType);
        }

        internal static IntPtr FPDF_LoadPage(IntPtr document, int pageIndex)
        {
            lock (SyncRoot)
                return Imports.FPDF_LoadPage(document, pageIndex);
        }

        internal static IntPtr FPDFText_LoadPage(IntPtr page)
        {
            lock (SyncRoot)
                return Imports.FPDFText_LoadPage(page);
        }

        internal static void FORM_OnAfterLoadPage(IntPtr page, IntPtr form)
        {
            lock (SyncRoot)
                Imports.FORM_OnAfterLoadPage(page, form);
        }

        internal static void FORM_DoPageAAction(IntPtr page, IntPtr form, FpdfPageAAction action)
        {
            lock (SyncRoot)
                Imports.FORM_DoPageAAction(page, form, (int)action);
        }

        internal static double FPDF_GetPageWidth(IntPtr page)
        {
            lock (SyncRoot)
                return Imports.FPDF_GetPageWidth(page);
        }

        internal static double FPDF_GetPageHeight(IntPtr page)
        {
            lock (SyncRoot)
                return Imports.FPDF_GetPageHeight(page);
        }

        internal static void FORM_OnBeforeClosePage(IntPtr page, IntPtr form)
        {
            lock (SyncRoot)
                Imports.FORM_OnBeforeClosePage(page, form);
        }

        internal static void FPDFText_ClosePage(IntPtr textPage)
        {
            lock (SyncRoot)
                Imports.FPDFText_ClosePage(textPage);
        }

        internal static void FPDF_ClosePage(IntPtr page)
        {
            lock (SyncRoot)
                Imports.FPDF_ClosePage(page);
        }

        internal static void FPDF_RenderPageBitmap(IntPtr bitmapHandle, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags)
        {
            lock (SyncRoot)
                Imports.FPDF_RenderPageBitmap(bitmapHandle, page, startX, startY, sizeX, sizeY, rotate, flags);
        }

        internal static int FPDF_GetPageSizeByIndex(IntPtr document, int pageIndex, out double width, out double height)
        {
            lock (SyncRoot)
                return Imports.FPDF_GetPageSizeByIndex(document, pageIndex, out width, out height);
        }

        internal static IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride)
        {
            lock (SyncRoot)
                return Imports.FPDFBitmap_CreateEx(width, height, format, firstScan, stride);
        }

        internal static void FPDFBitmap_FillRect(IntPtr bitmapHandle, int left, int top, int width, int height, uint color)
        {
            lock (SyncRoot)
                Imports.FPDFBitmap_FillRect(bitmapHandle, left, top, width, height, color);
        }

        internal static void FPDFBitmap_Destroy(IntPtr bitmapHandle)
        {
            lock (SyncRoot)
                Imports.FPDFBitmap_Destroy(bitmapHandle);
        }

        internal static void FPDF_FFLDraw(IntPtr form, IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags)
        {
            lock (SyncRoot)
                Imports.FPDF_FFLDraw(form, bitmap, page, startX, startY, sizeX, sizeY, rotate, flags);
        }

        internal static int FPDFText_GetText(IntPtr page, int startIndex, int count, byte[] result)
        {
            lock (SyncRoot)
                return Imports.FPDFText_GetText(page, startIndex, count, result);
        }

        internal static void FPDFText_GetCharBox(IntPtr page, int index, out double left, out double right, out double bottom, out double top)
        {
            lock (SyncRoot)
                Imports.FPDFText_GetCharBox(page, index, out left, out right, out bottom, out top);
        }

        internal static int FPDFText_CountChars(IntPtr page)
        {
            lock (SyncRoot)
                return Imports.FPDFText_CountChars(page);
        }

        internal static void FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, double pageX, double pageY, out int deviceX, out int deviceY)
        {
            lock (SyncRoot)
                Imports.FPDF_PageToDevice(page, startX, startY, sizeX, sizeY, rotate, pageX, pageY, out deviceX, out deviceY);
        }

        internal static void FPDF_DeviceToPage(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int deviceX, int deviceY, out double pageX, out double pageY)
        {
            lock (SyncRoot)
                Imports.FPDF_DeviceToPage(page, startX, startY, sizeX, sizeY, rotate, deviceX, deviceY, out pageX, out pageY);
        }

        internal static uint FPDF_GetLastError()
        {
            lock (SyncRoot)
                return Imports.FPDF_GetLastError();
        }

        #endregion

        [StructLayout(LayoutKind.Sequential)]
        private struct FPDF_FILEACCESS
        {
            public uint m_FileLen;
            public IntPtr m_GetBlock;
            public IntPtr m_Param;
        }

        /// <summary>
        /// FPDF_FORMFILLINFO: int version + 31 function-pointer slots
        /// (v1 uses the first 16; XFA v2 adds 15 more). Populated by
        /// <see cref="CreateFormFillInfo"/> before being pinned and passed
        /// by address — field order and types mirror fpdf_formfill.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FpdfFormFillInfo
        {
            public int Version;
#pragma warning disable 169, 649
            public IntPtr Release;
            public IntPtr FFI_Invalidate;
            public IntPtr FFI_OutputSelectedRect;
            public IntPtr FFI_SetCursor;
            public IntPtr FFI_SetTimer;
            public IntPtr FFI_KillTimer;
            public IntPtr FFI_GetLocalTime;
            public IntPtr FFI_OnChange;
            public IntPtr FFI_GetPage;
            public IntPtr FFI_GetCurrentPage;
            public IntPtr FFI_GetRotation;
            public IntPtr FFI_ExecuteNamedAction;
            public IntPtr FFI_SetTextFieldFocus;
            public IntPtr FFI_DoURIAction;
            public IntPtr FFI_DoGoToAction;
            public IntPtr m_pJsPlatform;
            // XFA support (version 2)
            private IntPtr FFI_DisplayCaret;
            private IntPtr FFI_GetCurrentPageIndex;
            private IntPtr FFI_SetCurrentPage;
            private IntPtr FFI_GotoURL;
            private IntPtr FFI_GetPageViewRect;
            private IntPtr FFI_PageEvent;
            private IntPtr FFI_PopupMenu;
            private IntPtr FFI_OpenFile;
            private IntPtr FFI_EmailTo;
            private IntPtr FFI_UploadTo;
            private IntPtr FFI_GetPlatform;
            private IntPtr FFI_GetLanguage;
            private IntPtr FFI_DownloadFromURL;
            private IntPtr FFI_PostRequestURL;
            private IntPtr FFI_PutRequestURL;
#pragma warning restore 169, 649
        }

        #region FPDF_FORMFILLINFO callbacks (fpdf_formfill.h signatures)

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiReleaseDelegate(IntPtr pThis);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiInvalidateDelegate(IntPtr pThis, IntPtr page, double left, double top, double right, double bottom);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiOutputSelectedRectDelegate(IntPtr pThis, IntPtr page, IntPtr left, IntPtr top, IntPtr right, IntPtr bottom);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiSetCursorDelegate(IntPtr pThis, int cursorType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FfiSetTimerDelegate(IntPtr pThis, int elapse, IntPtr timerFunc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiKillTimerDelegate(IntPtr pThis, int timerId);

        // FPDF_SYSTEMTIME FFI_GetLocalTime(void* pThis) returns a 16-byte
        // struct BY VALUE. On the Win64 ABI a by-value aggregate >8 bytes
        // lowers to a hidden first parameter: caller allocates the return
        // storage, passes its pointer in RCX, and pThis shifts to RDX; the
        // callee writes the struct and returns that same pointer in RAX.
        // The previous (IntPtr pThis)->IntPtr signature left the caller's
        // buffer unwritten, so pdfium read uninitialized stack whenever it
        // queried local time (AcroForm date fields, doc-JS util.printd —
        // FORM_DoDocumentJSAction runs on every open). Latent UMR.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr FfiGetLocalTimeDelegate(IntPtr outSystemTime, IntPtr pThis);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiOnChangeDelegate(IntPtr pThis);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr FfiGetPageDelegate(IntPtr pThis, IntPtr document, int pageIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr FfiGetCurrentPageDelegate(IntPtr pThis, IntPtr document);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FfiGetRotationDelegate(IntPtr pThis, IntPtr page);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiExecuteNamedActionDelegate(IntPtr pThis, IntPtr namedAction);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiSetTextFieldFocusDelegate(IntPtr pThis, IntPtr value, uint valueLen, int isFocus);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiDoUriActionDelegate(IntPtr pThis, IntPtr uri);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FfiDoGoToActionDelegate(IntPtr pThis, int pageIndex, int zoomMode, IntPtr posArray, int arraySize);

        // Keep the delegates alive for the process lifetime; form envs are
        // process-wide in practice and the callbacks are stateless, so
        // sharing is safe.
        private static readonly object[] FormFillCallbacks;

        static PdfiumNative()
        {
            FormFillCallbacks = new object[]
            {
                new FfiReleaseDelegate(_ => { }),
                new FfiInvalidateDelegate((p, page, l, t, r, b) => { }),
                new FfiOutputSelectedRectDelegate((p, page, l, t, r, b) => { }),
                new FfiSetCursorDelegate((p, cursorType) => { }),
                new FfiSetTimerDelegate((p, elapse, func) => 0),
                new FfiKillTimerDelegate((p, timerId) => { }),
                new FfiGetLocalTimeDelegate(FfiGetLocalTime),
                new FfiOnChangeDelegate(_ => { }),
                new FfiGetPageDelegate((p, doc, pageIndex) => IntPtr.Zero),
                new FfiGetCurrentPageDelegate((p, doc) => IntPtr.Zero),
                new FfiGetRotationDelegate((p, page) => 0),
                new FfiExecuteNamedActionDelegate((p, action) => { }),
                new FfiSetTextFieldFocusDelegate((p, value, len, focus) => { }),
                new FfiDoUriActionDelegate((p, uri) => { }),
                new FfiDoGoToActionDelegate((p, pageIndex, zoom, pos, size) => { }),
            };
        }

        /// <summary>
        /// FPDF_SYSTEMTIME = 8 ushorts (wYear since 1900, wMonth 0-11,
        /// wDayOfWeek 0-6, wDay 1-31, wHour, wMinute, wSecond, wMilliseconds)
        /// caller-owned buffer (see the hidden-out-param note on the
        /// delegate). Writing past byte 15 overflows pdfium's 16-byte
        /// stack buffer. Returns the buffer pointer as the ABI requires.
        /// </summary>
        private static IntPtr FfiGetLocalTime(IntPtr outSystemTime, IntPtr pThis)
        {
            DateTime now = DateTime.Now;
            Marshal.WriteInt16(outSystemTime, 0, (short)(now.Year - 1900));
            Marshal.WriteInt16(outSystemTime, 2, (short)(now.Month - 1));
            Marshal.WriteInt16(outSystemTime, 4, (short)now.DayOfWeek);
            Marshal.WriteInt16(outSystemTime, 6, (short)now.Day);
            Marshal.WriteInt16(outSystemTime, 8, (short)now.Hour);
            Marshal.WriteInt16(outSystemTime, 10, (short)now.Minute);
            Marshal.WriteInt16(outSystemTime, 12, (short)now.Second);
            Marshal.WriteInt16(outSystemTime, 14, (short)now.Millisecond);
            return outSystemTime;
        }

        /// <summary>
        /// Builds an FPDF_FORMFILLINFO whose 15 v1 callback slots point at the
        /// shared stateless delegates above (m_pJsPlatform stays null — no JS
        /// platform, same as PdfiumViewer).
        /// </summary>
        internal static FpdfFormFillInfo CreateFormFillInfo()
        {
            return new FpdfFormFillInfo
            {
                Release = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[0]),
                FFI_Invalidate = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[1]),
                FFI_OutputSelectedRect = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[2]),
                FFI_SetCursor = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[3]),
                FFI_SetTimer = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[4]),
                FFI_KillTimer = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[5]),
                FFI_GetLocalTime = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[6]),
                FFI_OnChange = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[7]),
                FFI_GetPage = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[8]),
                FFI_GetCurrentPage = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[9]),
                FFI_GetRotation = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[10]),
                FFI_ExecuteNamedAction = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[11]),
                FFI_SetTextFieldFocus = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[12]),
                FFI_DoURIAction = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[13]),
                FFI_DoGoToAction = Marshal.GetFunctionPointerForDelegate((Delegate)FormFillCallbacks[14]),
                m_pJsPlatform = IntPtr.Zero,
            };
        }

        #endregion

        private static class Imports
        {
            // pdfium headers declare FPDF_CALLCONV = __stdcall on Windows
            // (x64 collapses cdecl/stdcall into one convention, but StdCall
            // matches the spec and is the only correct form on x86).
            private const CallingConvention Convention = CallingConvention.StdCall;

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_AddRef();

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_Release();

            [DllImport(PdfiumDll, CallingConvention = Convention, CharSet = CharSet.Ansi)]
            public static extern IntPtr FPDF_LoadCustomDocument(in FPDF_FILEACCESS access, string password);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_CloseDocument(IntPtr document);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern int FPDF_GetPageCount(IntPtr document);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern uint FPDF_GetDocPermissions(IntPtr document);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr document, IntPtr formInfo);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_SetFormFieldHighlightColor(IntPtr hHandle, int fieldType, uint color);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_SetFormFieldHighlightAlpha(IntPtr hHandle, byte alpha);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_DoDocumentJSAction(IntPtr hHandle);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_DoDocumentOpenAction(IntPtr hHandle);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr hHandle);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_DoDocumentAAction(IntPtr hHandle, int aaType);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern IntPtr FPDFText_LoadPage(IntPtr page);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_OnAfterLoadPage(IntPtr page, IntPtr form);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_DoPageAAction(IntPtr page, IntPtr form, int action);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern double FPDF_GetPageWidth(IntPtr page);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern double FPDF_GetPageHeight(IntPtr page);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FORM_OnBeforeClosePage(IntPtr page, IntPtr form);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDFText_ClosePage(IntPtr textPage);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_ClosePage(IntPtr page);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_RenderPageBitmap(IntPtr bitmapHandle, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern int FPDF_GetPageSizeByIndex(IntPtr document, int pageIndex, out double width, out double height);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDFBitmap_FillRect(IntPtr bitmapHandle, int left, int top, int width, int height, uint color);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDFBitmap_Destroy(IntPtr bitmapHandle);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_FFLDraw(IntPtr form, IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern int FPDFText_GetText(IntPtr page, int startIndex, int count, byte[] result);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDFText_GetCharBox(IntPtr page, int index, out double left, out double right, out double bottom, out double top);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern int FPDFText_CountChars(IntPtr page);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, double pageX, double pageY, out int deviceX, out int deviceY);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern void FPDF_DeviceToPage(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int deviceX, int deviceY, out double pageX, out double pageY);

            [DllImport(PdfiumDll, CallingConvention = Convention)]
            public static extern uint FPDF_GetLastError();
        }
    }
}
