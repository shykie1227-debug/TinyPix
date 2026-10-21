using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TinyPix.App.Controls;

/// <summary>
/// Shell/TaskQueue — bottom task-queue region. Pages report the current job
/// through <see cref="ShowTask"/> / <see cref="ShowIdle"/>; the shell owns one
/// row because the design keeps a single active task visible at a time.
/// </summary>
public sealed partial class TaskQueueControl : UserControl
{
    /// <summary>The live instance, set by MainWindow so pages can report progress.</summary>
    public static TaskQueueControl? Current { get; private set; }

    public TaskQueueControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Current = this;
        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        };
    }

    public void ShowIdle()
    {
        QueueCountText.Text = "0";
        TaskTitleText.Text = "暂无进行中的任务";
        TaskSubtitleText.Text = "开始处理后在此显示进度";
        ProgressText.Text = "0%";
        ProgressFill.Width = 2;
        CancelButton.IsEnabled = false;
    }

    public void ShowTask(string title, string subtitle, int percent)
    {
        QueueCountText.Text = "1";
        TaskTitleText.Text = title;
        TaskSubtitleText.Text = subtitle;
        ProgressText.Text = $"{percent}%";

        double track = ProgressFill.Parent is Grid grid ? grid.ActualWidth - 44 : 0;
        ProgressFill.Width = track > 0 ? System.Math.Max(2, track * percent / 100.0) : 2;
        CancelButton.IsEnabled = true;
    }
}
