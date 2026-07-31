using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TinyPix.App.ViewModels;

namespace TinyPix.App.Pages;

/// <summary>
/// 视频输出 workbench page. Receives the shell MainWindowViewModel through
/// Frame navigation so page state and the shared task queue stay consistent.
/// </summary>
public sealed partial class VideoOutputPage : Page
{
    public VideoOutputPage()
    {
        InitializeComponent();
    }

    public MainWindowViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = e.Parameter as MainWindowViewModel;
    }
}
