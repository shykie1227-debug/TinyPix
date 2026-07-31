using System;
using Microsoft.UI.Xaml.Controls;
using TinyPix.App.ViewModels;

namespace TinyPix.App.Controls;

/// <summary>
/// Content of the single reusable SettingsDialog. The hosting ContentDialog sets
/// its XamlRoot to the app shell root so the modal scrim covers the complete
/// window; this control never creates a second dialog or a second window.
/// </summary>
public sealed partial class SettingsDialogContent : UserControl
{
    public SettingsDialogContent(IServiceProvider services)
    {
        ViewModel = new SettingsDialogViewModel(services);
        InitializeComponent();
        _ = ViewModel.LoadAsync();
    }

    public SettingsDialogViewModel ViewModel { get; }
}
