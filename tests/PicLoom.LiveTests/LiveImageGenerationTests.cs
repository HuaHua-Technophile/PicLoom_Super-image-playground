using PicLoom.Core;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace PicLoom.LiveTests;

/// <summary>
/// Three live smoke tests that exercise the whole pipeline against the configured relay. Nothing is
/// stubbed: there is no fake handler and no replayed fixture, so each test makes exactly one real
/// upstream call. That is why the suite is opt-in and skipped when this machine has no credentials.
/// </summary>
/// <remarks>
/// Run explicitly with:
/// <code>
/// dotnet test tests/PicLoom.LiveTests --filter Category=Live
/// </code>
/// Every failure message carries the task state and the captured structured log lines (request shape,
/// HTTP status, body snippet, elapsed time), which is the intended way to diagnose a bad relay.
/// </remarks>
public sealed class LiveImageGenerationTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    private readonly ITestOutputHelper _output;

    public LiveImageGenerationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Text-to-image with nothing but a prompt and a size: the body must be JSON posted to
    /// <c>/v1/images/generations</c> and the upstream must return at least one decodable image.
    /// </summary>
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task TextToImage_DefaultParameters()
    {
        using var timeout = new CancellationTokenSource(RunTimeout);
        var ct = timeout.Token;
        await using var harness = await StartHarnessAsync(ct);

        GenerationTask? task = null;
        try
        {
            task = await harness.RunTaskAsync(
                "A single red apple on a white table, soft studio light, product photo",
                Tier("1K", "1:1"),
                options: null,
                referenceIds: null,
                ct).ConfigureAwait(false);

            Assert.True(task.Status == GenerationStatus.Succeeded, harness.Describe(task));
            Assert.True(task.Assets.Count >= 1, harness.Describe(task));
            AssertAllAssetsDecodable(harness, task);

            Assert.True(harness.Logs.Contains("provider.dispatch"),
                "no provider.dispatch line was logged" + Environment.NewLine + harness.Describe(task));
            Assert.True(harness.Logs.Contains("transport=json"), harness.Describe(task));
            Assert.True(harness.Logs.Contains("path=/v1/images/generations"), harness.Describe(task));
            Assert.True(harness.Logs.Contains("provider.success"), harness.Describe(task));
            Assert.True(harness.Logs.Contains("status=200"), harness.Describe(task));

            _output.WriteLine(harness.Describe(task));
        }
        catch (Exception)
        {
            _output.WriteLine(harness.Describe(task));
            throw;
        }
    }

    /// <summary>
    /// Image-to-image with one reference image: the request must switch to multipart on
    /// <c>/v1/images/edits</c> and still produce at least one image.
    /// </summary>
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task ImageToImage_OneReference()
    {
        using var timeout = new CancellationTokenSource(RunTimeout);
        var ct = timeout.Token;
        await using var harness = await StartHarnessAsync(ct);

        GenerationTask? task = null;
        try
        {
            var referenceId = await harness.CreateReferenceAsync(LivePng.Create(64, 64), ct)
                .ConfigureAwait(false);

            task = await harness.RunTaskAsync(
                "Repaint this reference image as a flat vector illustration",
                Tier("1K", "1:1"),
                options: null,
                referenceIds: [referenceId],
                ct).ConfigureAwait(false);

            Assert.True(task.Status == GenerationStatus.Succeeded, harness.Describe(task));
            Assert.True(task.ExpectedReferenceCount == 1, harness.Describe(task));
            Assert.True(task.References.Count == 1, harness.Describe(task));
            Assert.True(task.Assets.Count >= 1, harness.Describe(task));
            AssertAllAssetsDecodable(harness, task);

            Assert.True(harness.Logs.Contains("transport=multipart"),
                "the request did not switch to multipart" + Environment.NewLine + harness.Describe(task));
            Assert.True(harness.Logs.Contains("path=/v1/images/edits"),
                "the request did not go to /v1/images/edits" + Environment.NewLine + harness.Describe(task));
            Assert.True(harness.Logs.Contains("references=1"), harness.Describe(task));
            Assert.True(harness.Logs.Contains("provider.success"), harness.Describe(task));

            _output.WriteLine(harness.Describe(task));
        }
        catch (Exception)
        {
            _output.WriteLine(harness.Describe(task));
            throw;
        }
    }

    /// <summary>
    /// <c>count=2</c>: the upstream may answer with fewer images than requested, so only "at least
    /// one" is mandatory. The interesting assertion is that the persisted row count equals the
    /// recorded <c>ActualCount</c>, i.e. nothing is dropped or invented on the way to disk.
    /// </summary>
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task MultiImage_Count2()
    {
        using var timeout = new CancellationTokenSource(RunTimeout);
        var ct = timeout.Token;
        await using var harness = await StartHarnessAsync(ct);

        GenerationTask? task = null;
        try
        {
            task = await harness.RunTaskAsync(
                "Two ceramic cups on a wooden shelf, natural daylight",
                Tier("1K", "1:1"),
                new GenerationOptionsInput(Count: 2),
                referenceIds: null,
                ct).ConfigureAwait(false);

            Assert.True(task.Status == GenerationStatus.Succeeded, harness.Describe(task));
            Assert.True(task.Options is { Count: 2 }, harness.Describe(task));
            Assert.True(task.Assets.Count >= 1,
                "the upstream returned no images at all" + Environment.NewLine + harness.Describe(task));
            Assert.True(task.ActualCount == task.Assets.Count,
                "persisted asset rows disagree with ActualCount" + Environment.NewLine + harness.Describe(task));
            AssertAllAssetsDecodable(harness, task);

            Assert.True(harness.Logs.Contains("provider.success"), harness.Describe(task));

            // Recorded on purpose: relays frequently answer n=2 with a single image.
            _output.WriteLine(
                $"ActualCount={task.ActualCount} requested=2 persistedRows={task.Assets.Count}");
            _output.WriteLine(harness.Describe(task));
        }
        catch (Exception)
        {
            _output.WriteLine(harness.Describe(task));
            throw;
        }
    }

    private static async Task<LiveHarness> StartHarnessAsync(CancellationToken ct)
    {
        var credentials = LiveCredentialsResolver.Current
                          ?? throw new XunitException(
                              "live credentials disappeared between discovery and execution: " +
                              LiveCredentialsResolver.SkipReason);
        return await LiveHarness.StartAsync(credentials, ct).ConfigureAwait(false);
    }

    private static ResolutionTarget Tier(string imageSize, string aspectRatio) =>
        new(ImageSize: imageSize, AspectRatio: aspectRatio, Mode: ResolutionSizing.ModeTier);

    private static void AssertAllAssetsDecodable(LiveHarness harness, GenerationTask task)
    {
        foreach (var asset in task.Assets)
        {
            var ok = harness.AssetIsDecodable(asset, out var detail);
            Assert.True(ok, $"asset is not a decodable image: {detail}" + Environment.NewLine +
                            harness.Describe(task));
        }
    }
}
