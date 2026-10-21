using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyPix.Core.Jobs;
using TinyPix.Core.Tools;
using TinyPix.Media.IdPhoto;
using Xunit;

namespace TinyPix.Media.Tests;

public sealed class IdPhotoHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinypix-idphoto-tests", Guid.NewGuid().ToString("N"));

    public IdPhotoHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private JobRequest Request(string toolId, IReadOnlyList<string> inputs, Dictionary<string, string>? parameters = null) =>
        JobRequest.Create(toolId, inputs, _root, JobWorkload.HeavyMedia, parameters ?? new Dictionary<string, string>());

    // ── Templates ──────────────────────────────────────────────────────────

    [Fact]
    public void Templates_ContainSevenPresets()
    {
        Assert.Equal(7, IdPhotoTemplates.All.Count);
    }

    [Fact]
    public void Templates_Office1in_HasCorrectPixels()
    {
        IdPhotoPreset preset = IdPhotoTemplates.Find("office-1in")!;
        Assert.Equal(295, preset.OutputWidthPx);
        Assert.Equal(413, preset.OutputHeightPx);
        Assert.Equal(300, preset.Dpi);
    }

    [Fact]
    public void Templates_UsVisa_HasOfficialPixels()
    {
        IdPhotoPreset preset = IdPhotoTemplates.Find("us-visa")!;
        Assert.Equal(600, preset.OutputWidthPx);
        Assert.Equal(600, preset.OutputHeightPx);
    }

    [Fact]
    public void Templates_CnPassport_HasHeadRules()
    {
        IdPhotoPreset preset = IdPhotoTemplates.Find("cn-passport-travel")!;
        Assert.NotNull(preset.HeadRules.HeadWidthMinMm);
        Assert.Equal(15, preset.HeadRules.HeadWidthMinMm);
        Assert.Equal(22, preset.HeadRules.HeadWidthMaxMm);
    }

    [Fact]
    public void Templates_JpVisa_HasNoHeadThresholds()
    {
        IdPhotoPreset preset = IdPhotoTemplates.Find("jp-visa-cn")!;
        Assert.Null(preset.HeadRules.HeadWidthMinMm);
        Assert.Null(preset.HeadRules.HeadHeightRatioMin);
    }

    [Fact]
    public void Templates_Find_ReturnsNullForUnknown()
    {
        Assert.Null(IdPhotoTemplates.Find("unknown-preset"));
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_InvalidPreset_FailsValidation()
    {
        var validation = await new IdPhotoCreateHandler().ValidateAsync(
            Request("idphoto.create", new[] { Path.Combine(_root, "a.jpg") },
                new Dictionary<string, string> { ["preset"] = "bogus" }), CancellationToken.None);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "idphoto.create.preset-invalid");
    }

    [Fact]
    public async Task Create_MissingFile_FailsValidation()
    {
        var validation = await new IdPhotoCreateHandler().ValidateAsync(
            Request("idphoto.create", new[] { Path.Combine(_root, "missing.jpg") }), CancellationToken.None);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Check_MissingFile_FailsValidation()
    {
        var validation = await new IdPhotoCheckHandler().ValidateAsync(
            Request("idphoto.check", new[] { Path.Combine(_root, "missing.jpg") }), CancellationToken.None);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task Print_MissingFile_FailsValidation()
    {
        var validation = await new IdPhotoPrintHandler().ValidateAsync(
            Request("idphoto.print", new[] { Path.Combine(_root, "missing.jpg") }), CancellationToken.None);
        Assert.False(validation.IsValid);
    }
}
