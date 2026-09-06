using System;
using System.Runtime.InteropServices;

namespace Caelum.Services
{
    internal static class RecycleBinService
    {
        private const uint FoDelete = 0x0003;
        private const ushort FofSilent = 0x0004;
        private const ushort FofNoConfirmation = 0x0010;
        private const ushort FofAllowUndo = 0x0040;
        private const ushort FofNoErrorUi = 0x0400;

        public static string EncodeDoubleNullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "\0\0";

            return path.TrimEnd('\0') + "\0\0";
        }

        public static bool TrySendToRecycleBin(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return false;

            var fileOp = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FoDelete,
                pFrom = EncodeDoubleNullPath(path),
                pTo = null,
                fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi),
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = null
            };

            return SHFileOperation(ref fileOp) == 0 && !fileOp.fAnyOperationsAborted;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }
    }
}
