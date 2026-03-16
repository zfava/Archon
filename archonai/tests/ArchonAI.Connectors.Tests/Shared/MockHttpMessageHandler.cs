using System.Net;

namespace ArchonAI.Connectors.Tests.Shared;

/// <summary>
/// Reusable mock HTTP handler for testing connector API interactions.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses = new();
    private int _requestCount;

    public int RequestCount => _requestCount;

    public void SetResponse(HttpStatusCode statusCode, string content)
    {
        _responses.Clear();
        _responses.Enqueue((statusCode, content));
    }

    public void SetResponseSequence(IEnumerable<(HttpStatusCode, string)> responses)
    {
        _responses.Clear();
        foreach (var r in responses)
        {
            _responses.Enqueue(r);
        }
    }

    protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);

        if (_responses.Count == 0)
        {
            return global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"no mock response configured\"}")
            });
        }

        var (statusCode, content) = _responses.Dequeue();
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

        return global::System.Threading.Tasks.Task.FromResult(response);
    }
}
