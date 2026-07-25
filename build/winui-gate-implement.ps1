$ErrorActionPreference = 'Stop'

$gateRoot = Join-Path $env:TEMP 'TinyPix-WinUI-Gate'
$projectPath = Join-Path $gateRoot 'TinyPix.WinUIGate.csproj'

@'
<?xml version="1.0" encoding="utf-8"?>
<Application
    x:Class="TinyPix_WinUIGate.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
            <Style TargetType="Button">
                <Setter Property="MinHeight" Value="44" />
            </Style>
            <Style TargetType="ComboBox">
                <Setter Property="MinHeight" Value="44" />
            </Style>
            <Style TargetType="TextBox">
                <Setter Property="MinHeight" Value="44" />
                <Setter Property="IsSpellCheckEnabled" Value="False" />
            </Style>
        </ResourceDictionary>
    </Application.Resources>
</Application>
'@ | Set-Content -LiteralPath (Join-Path $gateRoot 'App.xaml') -Encoding utf8

@'
<?xml version="1.0" encoding="utf-8" ?>
<Page
    x:Class="TinyPix_WinUIGate.MainPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    AllowDrop="True"
    DragOver="OnDragOver"
    Drop="OnDrop"
    KeyDown="OnPageKeyDown"
    Loaded="OnLoaded"
    SizeChanged="OnPageSizeChanged"
    mc:Ignorable="d">
    <Grid x:Name="AppRoot" Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
        <Grid.RowDefinitions>
            <RowDefinition x:Name="TopRow" Height="56" />
            <RowDefinition Height="*" />
            <RowDefinition x:Name="QueueRow" Height="90" />
        </Grid.RowDefinitions>
        <VisualStateManager.VisualStateGroups>
            <VisualStateGroup>
                <VisualState x:Name="Compact">
                    <VisualState.Setters>
                        <Setter Target="ParameterRegion.Padding" Value="10" />
                        <Setter Target="ParameterStack.Spacing" Value="6" />
                        <Setter Target="ParameterActions.Margin" Value="0,8,0,0" />
                    </VisualState.Setters>
                </VisualState>
                <VisualState x:Name="Wide">
                    <VisualState.StateTriggers>
                        <AdaptiveTrigger MinWindowWidth="1000" />
                    </VisualState.StateTriggers>
                    <VisualState.Setters>
                        <Setter Target="LeftColumn.Width" Value="240" />
                        <Setter Target="RightColumn.Width" Value="280" />
                        <Setter Target="TopRow.Height" Value="64" />
                        <Setter Target="QueueRow.Height" Value="112" />
                    </VisualState.Setters>
                </VisualState>
            </VisualStateGroup>
            <VisualStateGroup>
                <VisualState x:Name="NormalTextScale" />
                <VisualState x:Name="HighTextScale">
                    <VisualState.Setters>
                        <Setter Target="AppTitleStack.Visibility" Value="Collapsed" />
                        <Setter Target="OfflineBadge.Visibility" Value="Collapsed" />
                        <Setter Target="TopRegion.Padding" Value="12,0" />
                        <Setter Target="TopNavigation.Margin" Value="0" />
                        <Setter Target="LeftColumn.Width" Value="240" />
                        <Setter Target="RightColumn.Width" Value="250" />
                        <Setter Target="FileRegion.Padding" Value="12" />
                        <Setter Target="PreviewRegion.Padding" Value="12" />
                        <Setter Target="ParameterRegion.Padding" Value="10" />
                        <Setter Target="ParameterStack.Spacing" Value="8" />
                    </VisualState.Setters>
                </VisualState>
            </VisualStateGroup>
        </VisualStateManager.VisualStateGroups>

        <Grid x:Name="TopRegion" Padding="20,0" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="0,0,0,1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel x:Name="AppTitleStack" VerticalAlignment="Center">
                <TextBlock FontSize="22" FontWeight="SemiBold" Text="TinyPix WinUI 门禁" />
                <TextBlock FontSize="11" Opacity="0.65" Text="可行性测试（非正式界面）" />
            </StackPanel>
            <StackPanel x:Name="TopNavigation" Grid.Column="1" Margin="32,0" VerticalAlignment="Center" Orientation="Horizontal" Spacing="8">
                <Button x:Name="ImageToolsButton" AutomationProperties.Name="图片工具" Content="图片工具" />
                <Button AutomationProperties.Name="视频工具" Content="视频工具" />
                <Button AutomationProperties.Name="工具箱" Content="工具箱" />
            </StackPanel>
            <StackPanel Grid.Column="2" VerticalAlignment="Center" Orientation="Horizontal" Spacing="8">
                <TextBlock x:Name="OfflineBadge" VerticalAlignment="Center" Text="● 离线" />
                <Button x:Name="SettingsButton" AutomationProperties.Name="打开设置" Click="OnSettingsClick" Content="⚙ 设置" />
            </StackPanel>
        </Grid>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name="LeftColumn" Width="170" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition x:Name="RightColumn" Width="210" />
            </Grid.ColumnDefinitions>

            <Grid x:Name="FileRegion" Padding="16" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="0,0,1,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <TextBlock FontSize="20" FontWeight="SemiBold" Text="文件" />
                <Button x:Name="AddFilesButton" Grid.Row="1" Margin="0,12,0,12" HorizontalAlignment="Stretch" AutomationProperties.Name="添加媒体文件" Click="OnAddFilesClick" Content="＋ 添加媒体文件" />
                <ListView x:Name="FilesList" Grid.Row="2" AutomationProperties.Name="文件列表" SelectionMode="Single" />
                <TextBlock x:Name="PortableStatus" Grid.Row="3" Margin="0,12,0,0" TextWrapping="Wrap" />
            </Grid>

            <Grid x:Name="PreviewRegion" Grid.Column="1" Padding="20">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <StackPanel>
                    <TextBlock FontSize="24" FontWeight="SemiBold" Text="本地媒体预览" />
                    <TextBlock Opacity="0.72" Text="按钮添加与拖放共用同一导入逻辑" />
                </StackPanel>
                <Border Grid.Row="1" Margin="0,16" Padding="12" CornerRadius="12" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <Grid>
                        <TextBlock x:Name="EmptyPreviewText" HorizontalAlignment="Center" VerticalAlignment="Center" Text="添加或拖入图片、视频" />
                        <Image x:Name="ImagePreview" Stretch="Uniform" Visibility="Collapsed" AutomationProperties.Name="图片预览" />
                        <MediaPlayerElement x:Name="VideoPreview" AreTransportControlsEnabled="True" AutoPlay="False" Visibility="Collapsed" AutomationProperties.Name="视频预览" />
                    </Grid>
                </Border>
                <Button x:Name="PreviewFocusTarget" Grid.Row="2" HorizontalAlignment="Left" AutomationProperties.AutomationId="PreviewFocusTarget" AutomationProperties.Name="中央预览区域" Click="OnPreviewAction" Content="预览区域（Enter 播放或暂停）" />
            </Grid>

            <Grid x:Name="ParameterRegion" Grid.Column="2" Padding="16" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1,0,0,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <StackPanel x:Name="ParameterStack" Spacing="16">
                <TextBlock FontSize="20" FontWeight="SemiBold" Text="输出设置" />
                <StackPanel Spacing="6">
                    <TextBlock Text="格式" />
                    <ComboBox x:Name="FormatCombo" AutomationProperties.Name="输出格式" SelectedIndex="0">
                        <ComboBoxItem Content="保持原格式" />
                        <ComboBoxItem Content="MP4" />
                        <ComboBoxItem Content="PNG" />
                    </ComboBox>
                </StackPanel>
                <StackPanel Spacing="6">
                    <TextBlock Text="主题验证" />
                    <ComboBox x:Name="ThemeCombo" AutomationProperties.Name="主题验证" SelectionChanged="OnThemeChanged" SelectedIndex="0">
                        <ComboBoxItem Content="跟随系统" />
                        <ComboBoxItem Content="浅色" />
                        <ComboBoxItem Content="深色" />
                    </ComboBox>
                </StackPanel>
                <StackPanel Spacing="6">
                    <TextBlock Text="输出目录" />
                    <TextBox x:Name="OutputPathBox" AutomationProperties.Name="输出目录" IsReadOnly="True" TextAlignment="Right" />
                </StackPanel>
                <ProgressBar x:Name="JobProgress" Minimum="0" Maximum="100" Value="0" AutomationProperties.Name="任务进度" />
                <TextBlock x:Name="StatusText" Text="等待文件" TextWrapping="Wrap" />
                </StackPanel>
                </ScrollViewer>
                <StackPanel x:Name="ParameterActions" Grid.Row="1" Margin="0,12,0,0" Spacing="8">
                    <Button x:Name="StartButton" HorizontalAlignment="Stretch" AutomationProperties.Name="开始本地验证任务" Click="OnStartClick" Content="开始本地验证任务" />
                    <StackPanel x:Name="SecondaryActions" Orientation="Vertical" Spacing="8">
                        <Button x:Name="CancelButton" HorizontalAlignment="Stretch" AutomationProperties.Name="取消任务" Click="OnCancelClick" Content="取消" IsEnabled="False" />
                        <Button x:Name="FailureButton" HorizontalAlignment="Stretch" AutomationProperties.Name="验证异常退出" Click="OnFailureClick" Content="异常测试" />
                    </StackPanel>
                </StackPanel>
            </Grid>
        </Grid>

        <Grid x:Name="QueueRegion" Grid.Row="2" Padding="16,10" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="0,1,0,0">
            <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /></Grid.RowDefinitions>
            <Grid ColumnSpacing="8">
                <TextBlock VerticalAlignment="Center" FontSize="18" FontWeight="SemiBold" Text="任务队列（Ctrl+J）" />
                <Button x:Name="QueueFocusTarget" HorizontalAlignment="Right" AutomationProperties.Name="任务队列区域" Content="聚焦队列" />
            </Grid>
            <ListView x:Name="QueueList" Grid.Row="1" AutomationProperties.Name="任务队列" />
        </Grid>
    </Grid>
</Page>
'@ | Set-Content -LiteralPath (Join-Path $gateRoot 'MainPage.xaml') -Encoding utf8

@'
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace TinyPix_WinUIGate;

public sealed partial class MainPage : Page
{
    private readonly string _portableRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly List<Control> _focusRegions = new();
    private StorageFile? _currentFile;
    private Process? _ffmpegProcess;
    private CancellationTokenSource? _jobCancellation;
    private bool _engineReady;
    private int _focusIndex = -1;
    private Button? _settingsTrigger;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTextScaleState();
        _focusRegions.AddRange([ImageToolsButton, AddFilesButton, PreviewFocusTarget, FormatCombo, QueueFocusTarget]);
        OutputPathBox.Text = Path.Combine(_portableRoot, "Output");
        ToolTipService.SetToolTip(OutputPathBox, OutputPathBox.Text);
        Directory.CreateDirectory(OutputPathBox.Text);
        Directory.CreateDirectory(Path.Combine(_portableRoot, "Cache"));
        Directory.CreateDirectory(Path.Combine(_portableRoot, "Config"));
        Directory.CreateDirectory(Path.Combine(_portableRoot, "Logs"));

        var portableFlag = Path.Combine(_portableRoot, "portable.flag");
        var writable = await ProbeWritableAsync(_portableRoot);
        PortableStatus.Text = File.Exists(portableFlag) && writable
            ? "● 便携根目录可写；无网络请求"
            : "⛔ portable.flag 缺失或目录不可写";
        _engineReady = await ValidateFfmpegAsync();
        StartButton.IsEnabled = File.Exists(portableFlag) && writable && _engineReady;
        AddLog($"START portable={File.Exists(portableFlag)} writable={writable} engineReady={_engineReady}");
    }

    private void ApplyTextScaleState()
    {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        var highTextScale = uiSettings.TextScaleFactor >= 1.5;
        VisualStateManager.GoToState(this, highTextScale ? "HighTextScale" : "NormalTextScale", false);
        if (highTextScale)
        {
            LeftColumn.Width = new GridLength(240);
            RightColumn.Width = new GridLength(250);
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        if (uiSettings.TextScaleFactor >= 1.5)
            DispatcherQueue.TryEnqueue(ApplyTextScaleState);
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".avi");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await ImportFileAsync(file, "picker");
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "添加到 TinyPix 本地工作台";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.OfType<StorageFile>().FirstOrDefault() is { } file)
            await ImportFileAsync(file, "drop");
    }

    private async Task ImportFileAsync(StorageFile file, string source)
    {
        _currentFile = file;
        FilesList.Items.Clear();
        FilesList.Items.Add($"{file.Name}\n{source} → shared ImportFileAsync");
        FilesList.SelectedIndex = 0;
        FilesList.Focus(FocusState.Programmatic);
        QueueList.Items.Add($"已导入：{file.Name}（{source}）");
        StatusText.Text = $"已添加 {file.Name}";
        AddLog($"IMPORT source={source} path={file.Path}");

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(extension))
        {
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            ImagePreview.Source = bitmap;
            ImagePreview.Visibility = Visibility.Visible;
            VideoPreview.Visibility = Visibility.Collapsed;
            EmptyPreviewText.Visibility = Visibility.Collapsed;
        }
        else
        {
            VideoPreview.Source = MediaSource.CreateFromStorageFile(file);
            VideoPreview.MediaPlayer.MediaFailed -= OnVideoMediaFailed;
            VideoPreview.MediaPlayer.MediaFailed += OnVideoMediaFailed;
            VideoPreview.Visibility = Visibility.Visible;
            ImagePreview.Visibility = Visibility.Collapsed;
            EmptyPreviewText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnVideoMediaFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(async () => await ShowVideoFallbackAsync()))
            AddLog("VIDEO_FALLBACK dispatch=failed");
    }

    private async Task ShowVideoFallbackAsync()
    {
        try
        {
            if (_currentFile is null) return;
            StatusText.Text = "系统解码失败，正在生成 FFmpeg 本地缩略图…";
            var thumbnail = Path.Combine(_portableRoot, "Cache", "video-fallback.png");
            var result = await RunFfmpegAsync($"-y -ss 0 -i \"{_currentFile.Path}\" -frames:v 1 \"{thumbnail}\"", false);
            if (result == 0 && File.Exists(thumbnail))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(thumbnail);
                using var stream = await storageFile.OpenAsync(FileAccessMode.Read);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
                VideoPreview.Visibility = Visibility.Collapsed;
                StatusText.Text = "已显示 FFmpeg 本地缩略图回退";
                AddLog("VIDEO_FALLBACK status=ready");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "视频预览失败；本地 FFmpeg 缩略图也未能生成";
            AddLog($"VIDEO_FALLBACK exception={ex.GetType().Name} message={ex.Message}");
        }
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _settingsTrigger = (Button)sender;
        var outputBox = new TextBox { Header = "默认输出目录", Text = OutputPathBox.Text };
        AutomationProperties.SetName(outputBox, "默认输出目录");
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(outputBox);
        content.Children.Add(new TextBlock { Text = "设置仅保存在本地；无账号、无遥测、无网络请求。", TextWrapping = TextWrapping.Wrap });

        var cancelButton = new Button { Content = "取消", Height = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        var saveButton = new Button { Content = "保存设置", Height = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(cancelButton, "取消");
        AutomationProperties.SetName(saveButton, "保存设置");
        var actions = new Grid { Height = 44, ColumnSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(saveButton, 1);
        actions.Children.Add(cancelButton);
        actions.Children.Add(saveButton);
        content.Children.Add(actions);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "设置",
            Content = content
        };

        bool TrySave()
        {
            if (string.IsNullOrWhiteSpace(outputBox.Text) || !Directory.Exists(outputBox.Text))
            {
                outputBox.Focus(FocusState.Programmatic);
                outputBox.Description = "目录不存在，设置未保存";
                return false;
            }
            try
            {
                SaveSettingsAtomically(outputBox.Text);
                OutputPathBox.Text = outputBox.Text;
                ToolTipService.SetToolTip(OutputPathBox, OutputPathBox.Text);
                return true;
            }
            catch (Exception ex)
            {
                outputBox.Focus(FocusState.Programmatic);
                outputBox.Description = $"保存失败：{ex.Message}";
                return false;
            }
        }

        cancelButton.Click += (_, _) => dialog.Hide();
        saveButton.Click += (_, _) => { if (TrySave()) dialog.Hide(); };
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                if (TrySave()) dialog.Hide();
                args.Handled = true;
            }
            else if (args.Key == Windows.System.VirtualKey.Escape)
            {
                dialog.Hide();
                args.Handled = true;
            }
        };
        dialog.Closed += (_, _) => _settingsTrigger?.Focus(FocusState.Programmatic);
        await dialog.ShowAsync();
    }

    private void SaveSettingsAtomically(string outputPath)
    {
        var configDir = Path.Combine(_portableRoot, "Config");
        Directory.CreateDirectory(configDir);
        var finalPath = Path.Combine(configDir, "settings.json");
        var tempPath = finalPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new { outputDirectory = outputPath, offline = true }));
        File.Move(tempPath, finalPath, true);
        AddLog("SETTINGS atomic-save=success");
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_currentFile is null)
        {
            StatusText.Text = "请先添加文件";
            AddFilesButton.Focus(FocusState.Programmatic);
            return;
        }
        var sourceHash = await HashAsync(_currentFile.Path);
        var output = Path.Combine(OutputPathBox.Text, Path.GetFileNameWithoutExtension(_currentFile.Name) + "-gate" + _currentFile.FileType);
        QueueList.Items.Add($"等待：{Path.GetFileName(output)}");
        var exitCode = await RunFfmpegAsync($"-y -re -i \"{_currentFile.Path}\" -map 0 -c copy -progress pipe:1 -nostats \"{output}\"", true);
        var afterHash = await HashAsync(_currentFile.Path);
        var unchanged = sourceHash == afterHash;
        StatusText.Text = exitCode == 0 && unchanged ? "成功；原文件 SHA-256 未改变" : $"失败：exit={exitCode}, sourceUnchanged={unchanged}";
        QueueList.Items.Add(StatusText.Text);
        AddLog($"JOB exit={exitCode} sourceUnchanged={unchanged}");
    }

    private async void OnFailureClick(object sender, RoutedEventArgs e)
    {
        var exitCode = await RunFfmpegAsync("-i Z:\\definitely-missing-input.mp4 -f null -", true);
        StatusText.Text = exitCode != 0 ? $"异常退出已隔离（exit {exitCode}）" : "异常退出验证失败";
        QueueList.Items.Add(StatusText.Text);
        AddLog($"JOB abnormalExit={exitCode} isolated={exitCode != 0}");
    }

    private async Task<int> RunFfmpegAsync(string arguments, bool showProgress)
    {
        var ffmpeg = Path.Combine(_portableRoot, "Engines", "ffmpeg.exe");
        if (!_engineReady || !File.Exists(ffmpeg) || new FileInfo(ffmpeg).Length < 1024 * 1024)
        {
            StatusText.Text = "FFmpeg 引擎缺失、损坏或无法启动";
            return -2;
        }
        _jobCancellation = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        JobProgress.IsIndeterminate = showProgress;
        var stderr = new List<string>();
        try
        {
            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            _ffmpegProcess.Start();
            var outputTask = Task.Run(async () =>
            {
                while (await _ffmpegProcess.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("out_time=") || line.StartsWith("out_time_us="))
                    {
                        AddLog($"JOB progressSample={line}");
                        DispatcherQueue.TryEnqueue(() => StatusText.Text = $"执行中：{line[(line.IndexOf('=') + 1)..]}");
                    }
                    if (line == "progress=end") DispatcherQueue.TryEnqueue(() => JobProgress.Value = 100);
                }
            });
            var errorTask = Task.Run(async () =>
            {
                while (await _ffmpegProcess.StandardError.ReadLineAsync() is { } line)
                {
                    if (stderr.Count < 40) stderr.Add(line);
                }
            });
            await _ffmpegProcess.WaitForExitAsync(_jobCancellation.Token);
            await Task.WhenAll(outputTask, errorTask);
            return _ffmpegProcess.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (_ffmpegProcess is { HasExited: false }) _ffmpegProcess.Kill(true);
            StatusText.Text = "已取消；进程树已清理";
            AddLog("JOB cancelled processTreeKilled=true");
            return -3;
        }
        finally
        {
            JobProgress.IsIndeterminate = false;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _jobCancellation?.Dispose();
            _jobCancellation = null;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _jobCancellation?.Cancel();

    private void OnPreviewAction(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Visibility != Visibility.Visible) return;
        if (VideoPreview.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
            VideoPreview.MediaPlayer.Pause();
        else
            VideoPreview.MediaPlayer.Play();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppRoot is null) return;
        AppRoot.RequestedTheme = ThemeCombo.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        AddLog($"THEME selectedIndex={ThemeCombo.SelectedIndex}");
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == Windows.System.VirtualKey.J)
        {
            QueueFocusTarget.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.F6)
        {
            _focusIndex = (_focusIndex + 1) % _focusRegions.Count;
            _focusRegions[_focusIndex].Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && _jobCancellation is not null)
        {
            _jobCancellation.Cancel();
            e.Handled = true;
        }
    }

    private async Task<bool> ProbeWritableAsync(string path)
    {
        try
        {
            var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private async Task<bool> ValidateFfmpegAsync()
    {
        var ffmpeg = Path.Combine(_portableRoot, "Engines", "ffmpeg.exe");
        if (!File.Exists(ffmpeg) || new FileInfo(ffmpeg).Length < 1024 * 1024)
        {
            StatusText.Text = "FFmpeg 引擎缺失或损坏";
            AddLog("ENGINE ready=false reason=missing-or-too-small");
            return false;
        }
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, "-version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var firstLine = await process.StandardOutput.ReadLineAsync();
            await process.WaitForExitAsync();
            var ready = process.ExitCode == 0 && firstLine?.StartsWith("ffmpeg version 8.1.2", StringComparison.Ordinal) == true;
            StatusText.Text = ready ? "FFmpeg 8.1.2 已就绪" : "FFmpeg 引擎版本或启动校验失败";
            AddLog($"ENGINE ready={ready} exit={process.ExitCode} version={firstLine}");
            return ready;
        }
        catch (Exception error)
        {
            StatusText.Text = "FFmpeg 引擎无法启动";
            AddLog($"ENGINE ready=false reason={error.GetType().Name}");
            return false;
        }
    }

    private void AddLog(string message)
    {
        try
        {
            var logDir = Path.Combine(_portableRoot, "Logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "gate.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only portable roots must remain usable for inspection while
            // processing stays blocked; logging cannot crash the UI thread.
        }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $gateRoot 'MainPage.xaml.cs') -Encoding utf8

@'
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace TinyPix_WinUIGate;

public sealed partial class MainWindow : Window
{
    private readonly nint _windowHandle;
    private bool _initialPlacementComplete;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    public MainWindow()
    {
        InitializeComponent();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = "TinyPix WinUI 可行性测试（非正式界面）";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        Activated += OnWindowActivated;
        RootFrame.Navigate(typeof(MainPage));
    }

    private int ScaleDip(int value) =>
        (int)Math.Round(value * GetDpiForWindow(_windowHandle) / 96.0, MidpointRounding.AwayFromZero);

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_initialPlacementComplete || args.WindowActivationState == WindowActivationState.Deactivated)
            return;

        _initialPlacementComplete = true;
        Activated -= OnWindowActivated;
        PlaceInitialWindow();
        AppWindow.Changed += OnAppWindowChanged;
    }

    private void PlaceInitialWindow()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var size = GetInitialWindowSize(workArea);
        var x = workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
    }

    private SizeInt32 GetInitialWindowSize(RectInt32 workArea)
    {
        var wide = new SizeInt32(ScaleDip(1200), ScaleDip(800));
        if (wide.Width <= workArea.Width && wide.Height <= workArea.Height)
            return wide;
        return new SizeInt32(ScaleDip(900), ScaleDip(600));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        var width = Math.Max(sender.Size.Width, ScaleDip(900));
        var height = Math.Max(sender.Size.Height, ScaleDip(600));
        if (width != sender.Size.Width || height != sender.Size.Height)
            sender.Resize(new SizeInt32(width, height));
    }
}
'@ | Set-Content -LiteralPath (Join-Path $gateRoot 'MainWindow.xaml.cs') -Encoding utf8

@'
using Microsoft.UI.Xaml;

namespace TinyPix_WinUIGate;

public partial class App : Application
{
    private static Window? _window;
    public static nint WindowHandle => _window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(_window);

    public App() => InitializeComponent();

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
'@ | Set-Content -LiteralPath (Join-Path $gateRoot 'App.xaml.cs') -Encoding utf8

$engineDir = Join-Path $gateRoot 'Engines'
New-Item -ItemType Directory -Force -Path $engineDir | Out-Null
$ffmpegZip = Join-Path $env:APPDATA 'TinyPix\cache\ffmpeg\ffmpeg-8.1.2-essentials_build.zip'
$ffmpegSource = Join-Path $env:APPDATA 'TinyPix\sidecars\ffmpeg.exe'
$expectedZipHash = 'DB580001CAA24AC104C8CB856CD113A87B0A443F7BDF47D8C12B1D740584A2EC'
if (-not (Test-Path -LiteralPath $ffmpegZip) -or (Get-FileHash -Algorithm SHA256 -LiteralPath $ffmpegZip).Hash -ne $expectedZipHash) {
    throw 'The project-locked FFmpeg 8.1.2 package is missing or failed SHA-256 verification.'
}
if (-not (Test-Path -LiteralPath $ffmpegSource) -or (Get-Item -LiteralPath $ffmpegSource).Length -lt 1MB) {
    throw 'The verified FFmpeg package has not produced a usable ffmpeg.exe sidecar.'
}
$versionLine = & $ffmpegSource -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or $versionLine -notlike 'ffmpeg version 8.1.2*') {
    throw "The FFmpeg sidecar failed its executable/version check: $versionLine"
}
Copy-Item -LiteralPath $ffmpegSource -Destination (Join-Path $engineDir 'ffmpeg.exe') -Force
Set-Content -LiteralPath (Join-Path $gateRoot 'portable.flag') -Value 'TinyPix portable mode' -Encoding ascii

dotnet build $projectPath -c Debug -r win-x64
if ($LASTEXITCODE -ne 0) { throw "WinUI gate build failed with exit code $LASTEXITCODE." }
$outputRoot = Join-Path $gateRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64'
New-Item -ItemType Directory -Force -Path (Join-Path $outputRoot 'Engines') | Out-Null
Copy-Item -LiteralPath (Join-Path $engineDir 'ffmpeg.exe') -Destination (Join-Path $outputRoot 'Engines\ffmpeg.exe') -Force
Set-Content -LiteralPath (Join-Path $outputRoot 'portable.flag') -Value 'TinyPix portable mode' -Encoding ascii
Write-Output "GATE_OUTPUT=$outputRoot"
