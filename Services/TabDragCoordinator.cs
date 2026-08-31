using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Caelum.Models;

namespace Caelum.Services
{
    /// <summary>
    /// Coordinates one in-process tab drag across every OpenNotes window.
    /// The payload deliberately keeps the live AppTab reference so docking
    /// never reconstructs an EditorPage or loses its Frame journal state.
    /// </summary>
    public static class TabDragCoordinator
    {
        public const string DragDataFormat = "Caelum.AppTabDrag";

        private static readonly object Sync = new object();
        private static readonly HashSet<MainWindow> RegisteredWindows = new HashSet<MainWindow>();
        private static TabDragPayload _activePayload;
        private static bool _dropAccepted;
        private static bool _cancelled;

        public static IReadOnlyList<MainWindow> GetRegisteredWindows()
        {
            lock (Sync)
                return RegisteredWindows.ToList();
        }

        public static void Register(MainWindow window)
        {
            if (window == null)
                return;

            lock (Sync)
                RegisteredWindows.Add(window);
        }

        public static void Unregister(MainWindow window)
        {
            if (window == null)
                return;

            lock (Sync)
            {
                RegisteredWindows.Remove(window);
                if (_activePayload?.SourceWindow == window)
                {
                    _cancelled = true;
                    _activePayload = null;
                    _dropAccepted = false;
                }
            }
        }

        public static TabDragPayload BeginDrag(MainWindow sourceWindow, AppTab tab)
        {
            if (tab == null)
                throw new ArgumentNullException(nameof(tab));

            lock (Sync)
            {
                _activePayload = new TabDragPayload(sourceWindow, tab);
                _dropAccepted = false;
                _cancelled = false;
                return _activePayload;
            }
        }

        public static bool TryGetPayload(IDataObject data, out TabDragPayload payload)
        {
            payload = null;
            if (data == null || !data.GetDataPresent(DragDataFormat))
                return false;

            if (!(data.GetData(DragDataFormat) is TabDragPayload candidate))
                return false;

            lock (Sync)
            {
                if (!ReferenceEquals(candidate, _activePayload) || _cancelled)
                    return false;

                payload = candidate;
                return true;
            }
        }

        /// <summary>
        /// A destination calls this only after it has successfully taken
        /// ownership of the live tab and frame.
        /// </summary>
        public static bool AcceptDrop(TabDragPayload payload)
        {
            if (payload == null)
                return false;

            lock (Sync)
            {
                if (!ReferenceEquals(payload, _activePayload) || _cancelled)
                    return false;

                _dropAccepted = true;
                return true;
            }
        }

        public static void CancelDrag(TabDragPayload payload)
        {
            lock (Sync)
            {
                if (payload == null || ReferenceEquals(payload, _activePayload))
                    _cancelled = true;
            }
        }

        public static void CancelDrag()
        {
            lock (Sync)
            {
                _cancelled = true;
                _activePayload = null;
                _dropAccepted = false;
            }
        }

        /// <summary>
        /// Ends the source-side drag. Returns true only when WPF reported no
        /// destination effect and the drag was not cancelled or docked; the
        /// source should then create a detached window.
        /// </summary>
        public static bool CompleteDrag(TabDragPayload payload, DragDropEffects effect)
        {
            lock (Sync)
            {
                bool shouldDetach = ReferenceEquals(payload, _activePayload) &&
                    !_cancelled &&
                    !_dropAccepted &&
                    effect == DragDropEffects.None;

                if (ReferenceEquals(payload, _activePayload))
                {
                    _activePayload = null;
                    _dropAccepted = false;
                    _cancelled = false;
                }

                return shouldDetach;
            }
        }
    }

    public sealed class TabDragPayload
    {
        internal TabDragPayload(MainWindow sourceWindow, AppTab tab)
        {
            SourceWindow = sourceWindow;
            Tab = tab ?? throw new ArgumentNullException(nameof(tab));
        }

        public MainWindow SourceWindow { get; }

        public AppTab Tab { get; }

        public string TabId => Tab.Id;
    }
}
