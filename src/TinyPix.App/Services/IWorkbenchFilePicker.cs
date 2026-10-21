using System.Threading.Tasks;

namespace TinyPix.App.Services;

/// <summary>
/// Implemented by workbench pages that can add input files. The shell forwards
/// the left panel's primary "添加媒体文件" button to the active page through this
/// contract, so the button always acts on what the user is looking at.
/// </summary>
public interface IWorkbenchFilePicker
{
    Task AddFilesAsync();
}
