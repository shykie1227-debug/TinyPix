using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// 11 Card/File — reusable file row template (thumbnail + name + status +
/// remove). Bound to a file item view model via DataContext.
/// </summary>
public sealed partial class FileCardItem : UserControl
{
    public FileCardItem()
    {
        InitializeComponent();
    }
}
