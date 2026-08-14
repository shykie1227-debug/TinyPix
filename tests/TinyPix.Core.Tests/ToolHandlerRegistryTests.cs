using System;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;

namespace TinyPix.Core.Tests;

public sealed class ToolHandlerRegistryTests
{
    private sealed class StubHandler : IToolHandler
    {
        public StubHandler(string id) => ToolId = id;

        public string ToolId { get; }

        public ValueTask<ToolValidationResult> ValidateAsync(JobRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ToolValidationResult.Valid());

        public ValueTask<ToolEstimate> EstimateAsync(JobRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ToolEstimate(null, null));

        public Task<ToolExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(ToolExecutionResult.Success(new[] { "out" }));
    }

    [Fact]
    public void Register_and_resolve_round_trips_by_tool_id()
    {
        var registry = new ToolHandlerRegistry();
        var handler = new StubHandler("video.convert");

        registry.Register(handler);

        Assert.True(registry.IsRegistered("video.convert"));
        Assert.True(registry.TryResolve("video.convert", out IToolHandler? resolved));
        Assert.Equal("video.convert", resolved!.ToolId);
    }

    [Fact]
    public void Unknown_tool_id_is_not_resolved()
    {
        var registry = new ToolHandlerRegistry();

        Assert.False(registry.IsRegistered("video.gif"));
        Assert.False(registry.TryResolve("video.gif", out IToolHandler? resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void Empty_or_null_tool_id_is_rejected_without_throwing()
    {
        var registry = new ToolHandlerRegistry();

        Assert.False(registry.TryResolve(string.Empty, out _));
        Assert.False(registry.TryResolve(null, out _));
    }

    [Fact]
    public void Register_null_handler_throws()
    {
        var registry = new ToolHandlerRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void Re_registering_same_id_overwrites_previous_handler()
    {
        var registry = new ToolHandlerRegistry();
        registry.Register(new StubHandler("video.convert"));
        registry.Register(new StubHandler("video.compress"));

        Assert.True(registry.TryResolve("video.convert", out IToolHandler? resolved));
        Assert.Equal("video.compress", resolved!.ToolId);
    }
}
