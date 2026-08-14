using System.Collections.Generic;

namespace TinyPix.Core.Tools;

/// <summary>
/// Resolves the registered <see cref="IToolHandler"/> for a frozen ToolCatalog id.
/// Handlers are composed by the App shell (which owns the Media/Infrastructure
/// wiring) and registered here so the job queue can stay handler-agnostic.
/// </summary>
public sealed class ToolHandlerRegistry
{
    private readonly Dictionary<string, IToolHandler> _byId = new(StringComparer.Ordinal);

    public void Register(IToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _byId[handler.ToolId] = handler;
    }

    public bool TryResolve(string? toolId, out IToolHandler? handler)
    {
        handler = null;
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return false;
        }

        bool found = _byId.TryGetValue(toolId, out IToolHandler? resolved);
        handler = resolved;
        return found;
    }

    public bool IsRegistered(string? toolId) =>
        !string.IsNullOrWhiteSpace(toolId) && _byId.ContainsKey(toolId);
}
