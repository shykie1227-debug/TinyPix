using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// 19 State/Empty — empty/drop-target placeholder with a DragOver visual state.
/// </summary>
public sealed partial class EmptyStateControl : UserControl
{
    public EmptyStateControl()
    {
        InitializeComponent();
    }

    /// <summary>Switch to the DragOver appearance while files are hovered.</summary>
    public void SetDragOver(bool isDragOver) =>
        VisualStateManager.GoToState(this, isDragOver ? "DragOver" : "Empty", true);
}
