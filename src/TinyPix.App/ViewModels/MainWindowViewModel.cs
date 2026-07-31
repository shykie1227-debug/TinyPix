using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.App.ViewModels;

/// <summary>
/// Shell-level view model: owns the selected top category, the tool list fed by
/// the frozen ToolCatalog, and the job queue surface backed by JobQueueService.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly JobQueueService _jobQueue;

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        _jobQueue = services.GetRequiredService<JobQueueService>();
    }

    [ObservableProperty]
    private string _selectedCategory = "video";

    public IReadOnlyList<ToolDescriptor> VisibleTools =>
        ToolCatalog.All
            .Where(tool => MatchesCategory(tool, SelectedCategory))
            .ToList();

    public void SelectCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        SelectedCategory = category;
        OnPropertyChanged(nameof(VisibleTools));
    }

    public void TransportSeek(int direction)
    {
        // J/L seek contract; wired to the active timeline page in a later gate.
        System.Diagnostics.Debug.WriteLine($"transport.seek {direction}");
    }

    public void TransportToggle()
    {
        // K play/pause contract; wired to the active timeline page in a later gate.
        System.Diagnostics.Debug.WriteLine("transport.toggle");
    }

    private static bool MatchesCategory(ToolDescriptor tool, string category) =>
        category switch
        {
            "image" => tool.Category == ToolCategory.Image,
            "video" => tool.Category == ToolCategory.Video,
            "toolbox" => tool.Category is ToolCategory.Toolbox
                or ToolCategory.Pdf
                or ToolCategory.Recognition
                or ToolCategory.IdPhoto,
            _ => false,
        };
}
