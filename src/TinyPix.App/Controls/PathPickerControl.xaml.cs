using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// 9 Input/PathPicker — read-only path box + browse button, composing native
/// controls only (no new input template per the control mapping).
/// </summary>
public sealed partial class PathPickerControl : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(PathPickerControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register(nameof(Path), typeof(string), typeof(PathPickerControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BrowseCommandProperty =
        DependencyProperty.Register(nameof(BrowseCommand), typeof(ICommand), typeof(PathPickerControl), new PropertyMetadata(null));

    public static readonly DependencyProperty HasValidationErrorProperty =
        DependencyProperty.Register(nameof(HasValidationError), typeof(bool), typeof(PathPickerControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ValidationMessageProperty =
        DependencyProperty.Register(nameof(ValidationMessage), typeof(string), typeof(PathPickerControl), new PropertyMetadata(string.Empty));

    public PathPickerControl()
    {
        InitializeComponent();
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public ICommand? BrowseCommand
    {
        get => (ICommand?)GetValue(BrowseCommandProperty);
        set => SetValue(BrowseCommandProperty, value);
    }

    public bool HasValidationError
    {
        get => (bool)GetValue(HasValidationErrorProperty);
        set => SetValue(HasValidationErrorProperty, value);
    }

    public string ValidationMessage
    {
        get => (string)GetValue(ValidationMessageProperty);
        set => SetValue(ValidationMessageProperty, value);
    }
}
