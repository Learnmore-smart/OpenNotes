using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace Caelum.Controls
{
    /// <summary>
    /// Attached-property helper that assigns the Hand cursor to an element:
    /// <c>controls:CursorExtensions.Hand="True"</c>.
    ///
    /// <c>UIElement.ProtectedCursor</c> (Windows App SDK ≥ 1.5) is already
    /// pointer-over scoped — the framework applies it while the pointer is
    /// inside the element's hit-test region and restores the inherited cursor
    /// on exit — so a single property set covers the whole hover lifecycle
    /// with no PointerEntered/Exited handlers. It also flows through the
    /// visual tree: the deepest element under the pointer that defines a
    /// cursor wins, so setting it on a card root covers every child that
    /// doesn't override it.
    ///
    /// The property is <c>protected</c> — WinUI exposes it for control
    /// subclasses only — so the setter is invoked through reflection, the
    /// same workaround CommunityToolkit's WinUI cursor extension uses until
    /// the promised public <c>Cursor</c> property ships. One
    /// <see cref="InputSystemCursor"/> instance is shared by every consuming
    /// element — cursors are immutable, so no per-element allocation.
    /// </summary>
    public static class CursorExtensions
    {
        private static InputCursor s_hand;

        private static readonly PropertyInfo s_protectedCursor =
            typeof(UIElement).GetProperty(
                "ProtectedCursor",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static readonly DependencyProperty HandProperty =
            DependencyProperty.RegisterAttached(
                "Hand",
                typeof(bool),
                typeof(CursorExtensions),
                new PropertyMetadata(false, OnHandChanged));

        public static void SetHand(UIElement element, bool value)
            => element.SetValue(HandProperty, value);

        public static bool GetHand(UIElement element)
            => element.GetValue(HandProperty) is bool enabled && enabled;

        private static void OnHandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element || s_protectedCursor == null)
                return;

            s_protectedCursor.SetValue(
                element,
                e.NewValue is bool enabled && enabled ? GetHandCursor() : null);
        }

        private static InputCursor GetHandCursor()
            => s_hand ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
