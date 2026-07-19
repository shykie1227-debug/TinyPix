using TinyPix.Core.Jobs;

namespace TinyPix.Core.Tools;

public enum ToolCategory
{
    Video,
    Image,
    IdPhoto,
    Pdf,
    Recognition,
    Toolbox,
    Settings,
}

public enum ToolTemplate
{
    Conversion,
    Timeline,
    ImageEdit,
    IdPhoto,
    PdfPages,
    OcrAndCode,
    GeneratorAndFiles,
    Settings,
}

public sealed record ToolDescriptor(
    string Id,
    string DisplayName,
    ToolCategory Category,
    ToolTemplate Template,
    bool SupportsBatch,
    JobWorkload Workload);
