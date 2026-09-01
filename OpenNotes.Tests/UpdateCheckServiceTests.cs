using System.Net;
using System.Net.Http;
using System.Text;
using Caelum.Services;

namespace Caelum.Tests;

[TestFixture]
public sealed class UpdateCheckServiceTests
{
    [TestCase("v5.3.0", true, "5.3.0.0")]
    [TestCase("5.2.7", false, "5.2.7.0")]
    [TestCase("5.2.6.9", false, "5.2.6.9")]
    [TestCase("5.3", true, "5.3.0.0")]
    public async Task CheckAsyncNormalizesAndComparesReleaseVersions(
        string tag,
        bool expectedAvailable,
        string expectedVersion)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/Learnmore-smart/Windows-Notes/releases/tag/{{tag}}"}""")));
        using var client = new HttpClient(handler);
        var service = new UpdateCheckService(client);

        UpdateCheckResult result = await service.CheckAsync(new Version(5, 2, 7, 0));

        Assert.Multiple(() =>
        {
            Assert.That(result.InstalledVersion, Is.EqualTo(new Version(5, 2, 7, 0)));
            Assert.That(result.LatestVersion, Is.EqualTo(Version.Parse(expectedVersion)));
            Assert.That(result.IsUpdateAvailable, Is.EqualTo(expectedAvailable));
            Assert.That(result.ReleaseUri.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
        });
    }

    [Test]
    public async Task CheckAsyncSendsGitHubCompatibleRequestMetadata()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(
                """{"tag_name":"v5.3.0","html_url":"https://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0"}"""));
        });
        using var client = new HttpClient(handler);

        await new UpdateCheckService(client).CheckAsync(new Version(5, 2, 7, 0));

        Assert.That(captured, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(captured!.RequestUri, Is.EqualTo(UpdateCheckService.LatestReleaseApiUri));
            Assert.That(captured.Headers.Accept.Select(value => value.MediaType),
                Does.Contain("application/vnd.github+json"));
            Assert.That(captured.Headers.UserAgent.ToString(), Is.EqualTo($"OpenNotes/{ProductInfo.Version}"));
            Assert.That(captured.Headers.GetValues("X-GitHub-Api-Version"),
                Is.EqualTo(new[] { "2022-11-28" }));
        });
    }

    [TestCase("""{"tag_name":"5","html_url":"https://github.com/Learnmore-smart/Windows-Notes/releases/tag/5"}""")]
    [TestCase("""{"tag_name":"v5.3.0-beta","html_url":"https://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0-beta"}""")]
    [TestCase("""{"tag_name":"v5.3.0","html_url":"http://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0"}""")]
    [TestCase("""{"tag_name":"v5.3.0","html_url":"https://example.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0"}""")]
    [TestCase("""{"tag_name":"v5.3.0","html_url":"https://github.com/other/project/releases/tag/v5.3.0"}""")]
    [TestCase("""{"tag_name":"v5.3.0"}""")]
    [TestCase("""not json""")]
    public void CheckAsyncRejectsInvalidOrUnsafeReleasePayloads(string payload)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(payload)));
        using var client = new HttpClient(handler);
        var service = new UpdateCheckService(client);

        var error = Assert.ThrowsAsync<UpdateCheckException>(
            async () => await service.CheckAsync(new Version(5, 2, 7, 0)));

        Assert.That(error!.Kind, Is.EqualTo(UpdateCheckFailureKind.InvalidResponse));
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public void CheckAsyncCategorizesNonSuccessStatus(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(handler);

        var error = Assert.ThrowsAsync<UpdateCheckException>(
            async () => await new UpdateCheckService(client)
                .CheckAsync(new Version(5, 2, 7, 0)));

        Assert.That(error!.Kind, Is.EqualTo(UpdateCheckFailureKind.HttpStatus));
    }

    [Test]
    public void CheckAsyncCategorizesTransportFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        using var client = new HttpClient(handler);

        var error = Assert.ThrowsAsync<UpdateCheckException>(
            async () => await new UpdateCheckService(client)
                .CheckAsync(new Version(5, 2, 7, 0)));

        Assert.That(error!.Kind, Is.EqualTo(UpdateCheckFailureKind.Network));
    }

    [Test]
    public void CheckAsyncCategorizesInternalTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using var client = new HttpClient(handler);
        var service = new UpdateCheckService(client, TimeSpan.FromMilliseconds(20));

        var error = Assert.ThrowsAsync<UpdateCheckException>(
            async () => await service.CheckAsync(new Version(5, 2, 7, 0)));

        Assert.That(error!.Kind, Is.EqualTo(UpdateCheckFailureKind.Timeout));
    }

    [Test]
    public void CheckAsyncPreservesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(
            async () => await new UpdateCheckService(client)
                .CheckAsync(new Version(5, 2, 7, 0), cancellation.Token));
    }

    [TestCase("https://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0", true)]
    [TestCase("https://github.com/learnmore-smart/windows-notes/releases/latest", true)]
    [TestCase("http://github.com/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0", false)]
    [TestCase("https://github.com.evil.test/Learnmore-smart/Windows-Notes/releases/tag/v5.3.0", false)]
    [TestCase("https://github.com/Learnmore-smart/Windows-Notes/issues/1", false)]
    public void TrustedReleaseUriIsFailClosed(string value, bool expected)
    {
        Assert.That(UpdateCheckService.IsTrustedReleaseUri(new Uri(value)), Is.EqualTo(expected));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}
