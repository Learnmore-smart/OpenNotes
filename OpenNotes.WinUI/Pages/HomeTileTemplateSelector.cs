using Caelum.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Caelum.Pages
{
    /// <summary>
    /// Picks the AddTile / FolderTile / FileTile data template for a
    /// <see cref="HomeTile"/> — a straight port of the WPF
    /// <c>Pages/HomeTileTemplateSelector.cs</c>. Assigned to the
    /// <c>ItemsRepeater.ItemTemplate</c> on HomePage (WinUI ItemsRepeater
    /// accepts a <see cref="DataTemplateSelector"/> there).
    /// </summary>
    public sealed class HomeTileTemplateSelector : DataTemplateSelector
    {
        public DataTemplate AddTileTemplate { get; set; }

        public DataTemplate FolderTileTemplate { get; set; }

        public DataTemplate FileTileTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTileTemplate(item) ?? base.SelectTemplateCore(item, container);
        }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return SelectTileTemplate(item) ?? base.SelectTemplateCore(item);
        }

        private DataTemplate SelectTileTemplate(object item)
        {
            if (item is HomeTile tile && tile.IsAddTile && AddTileTemplate != null)
            {
                return AddTileTemplate;
            }

            if (item is HomeTile folderTile && folderTile.IsFolder && FolderTileTemplate != null)
            {
                return FolderTileTemplate;
            }

            if (FileTileTemplate != null)
            {
                return FileTileTemplate;
            }

            return AddTileTemplate;
        }
    }
}
