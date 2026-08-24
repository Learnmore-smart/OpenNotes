using System;
using System.Threading.Tasks;

namespace Caelum.Services;

/// <summary>
/// Small, UI-independent journal transition barrier.  A successful save
/// preparation is provisional until the navigation journal still permits the
/// requested transition; stale queued clicks cancel the close state so the
/// editor can resume input and autosave.
/// </summary>
public static class NavigationCloseCoordinator
{
    public static async Task<bool> TryNavigateBackAsync(
        Func<Task<bool>> prepareAsync,
        Func<bool> canGoBack,
        Action cancelPreparation,
        Func<Task> navigateAsync)
    {
        ArgumentNullException.ThrowIfNull(prepareAsync);
        ArgumentNullException.ThrowIfNull(canGoBack);
        ArgumentNullException.ThrowIfNull(cancelPreparation);
        ArgumentNullException.ThrowIfNull(navigateAsync);

        if (!await prepareAsync().ConfigureAwait(true))
            return false;

        if (!canGoBack())
        {
            cancelPreparation();
            return false;
        }

        await navigateAsync().ConfigureAwait(true);
        return true;
    }
}
