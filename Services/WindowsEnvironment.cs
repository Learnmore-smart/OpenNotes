using System;
using System.IO;

namespace Caelum.Services;

internal static class WindowsEnvironment
{
    internal static void NormalizeForWpf()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINDIR")))
            return;

        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot) || !Directory.Exists(systemRoot))
            return;

        // WPF's system-font bootstrap reads WINDIR directly. Some hosts expose
        // only SystemRoot, so fill the process-local alias before any Window is
        // initialized. This does not modify the user's or machine's environment.
        Environment.SetEnvironmentVariable(
            "WINDIR",
            systemRoot,
            EnvironmentVariableTarget.Process);
    }
}
