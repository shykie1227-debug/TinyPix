using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// 12 Card/Task — reusable job row template (progress + state + cancel/more).
/// Bound to a job view model via DataContext.
/// </summary>
public sealed partial class TaskCardItem : UserControl
{
    public TaskCardItem()
    {
        InitializeComponent();
    }
}
