using System.Net;
using System.Text;
using ForestBot.Llm;
using Xunit;

namespace ForestBot.Tests;

// T-0156: a model call that hits HttpClient.Timeout is "overloaded" (the chain rests the
// model and tries the next, or the runner gets the busy message) - it used to escape as
// TaskCanceledException: no Discord reply, the eval stopped with no score.
public class RetryTests
{
    /// Never answers until the request is cancelled.
    private sealed class Hang : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
    }

    private sealed class Status : HttpMessageHandler
    {
        public HttpStatusCode Code;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Code) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }

    private static HttpRequestMessage Req() => new HttpRequestMessage(HttpMethod.Post, "https://model.test/");

    [Fact]
    public async Task A_timeout_is_an_overloaded_model_not_retried()
    {
        Hang hang = new Hang();
        HttpClient http = new HttpClient(hang) { Timeout = TimeSpan.FromMilliseconds(200) };
        ModelUnavailableException e = await Assert.ThrowsAsync<ModelUnavailableException>(() => Retry.SendAsync(http, Req, CancellationToken.None));
        Assert.True(e.Overloaded);
        Assert.Equal(Retry.TimeoutRest, e.RetryAfter);
        Assert.Contains("timed out", e.Message);
        Assert.Equal(1, hang.Calls);
    }

    [Fact]
    public async Task The_callers_cancel_still_propagates()
    {
        HttpClient http = new HttpClient(new Hang()) { Timeout = TimeSpan.FromSeconds(30) };
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Retry.SendAsync(http, Req, cts.Token));
    }

    [Fact]
    public async Task Gemini_turns_a_timeout_into_ModelUnavailable()
    {
        HttpClient http = new HttpClient(new Hang()) { Timeout = TimeSpan.FromMilliseconds(200) };
        GeminiChat model = new GeminiChat(http, "flash", "key");
        ModelUnavailableException e = await Assert.ThrowsAsync<ModelUnavailableException>(() =>
            model.CompleteAsync("sys", new List<ChatMessage> { ChatMessage.User("q") }, null, 100, CancellationToken.None));
        Assert.True(e.Overloaded);
    }

    [Fact]
    public async Task Server_errors_keep_their_quick_retries()
    {
        TimeSpan[] keep = Retry.Delays;
        Retry.Delays = new[] { TimeSpan.Zero, TimeSpan.Zero };
        try
        {
            Status s = new Status { Code = HttpStatusCode.ServiceUnavailable };
            (HttpResponseMessage resp, _) = await Retry.SendAsync(new HttpClient(s), Req, CancellationToken.None);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            Assert.Equal(3, s.Calls);
        }
        finally { Retry.Delays = keep; }
    }
}
