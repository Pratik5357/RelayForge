using System.Net;
using System.Text;
using System.Text.Json;

namespace RelayForge.Handlers.BuiltIn;

/// <summary>
/// "http.request": calls a URL and classifies the response.
/// Payload: { "method": "POST", "url": "https://…", "headers": { … }, "body": { … } }.
/// Sends the task's IdempotencyKey as an Idempotency-Key header, so a retried call is
/// deduplicated by any receiver that supports it.
///
/// Retryable: timeouts, connection failures, 408, 429 (honoring Retry-After) and 5xx.
/// Fatal: every other non-2xx status (400, 401, 403, 404, 422, …), because sending the same
/// request again can't change the answer.
/// Output: { "status": 200, "body": &lt;JSON if the response was JSON, else text&gt; }.
/// </summary>
public sealed class HttpRequestHandler : ITaskHandler
{
    public const string ClientName = "relayforge.http.request";
    private const int MaxBodyChars = 64 * 1024;

    private readonly IHttpClientFactory _clients;

    public HttpRequestHandler(IHttpClientFactory clients)
    {
        _clients = clients;
    }

    public string Type => "http.request";

    private sealed record Options(string? Method, string? Url, Dictionary<string, string>? Headers, JsonElement? Body);

    public string? Validate(JsonElement payload)
    {
        Options? options;

        try
        {
            options = payload.Deserialize<Options>(HandlerJson.Options);
        }
        catch (JsonException ex)
        {
            return $"Invalid http.request payload: {ex.Message}";
        }

        if (options?.Url is null ||
            !Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "http.request needs an absolute http(s) \"url\".";
        }

        return null;
    }

    public async Task<TaskResult> ExecuteAsync(TaskContext context)
    {
        var options = context.Payload.Deserialize<Options>(HandlerJson.Options)!;
        var method = new HttpMethod((options.Method ?? "GET").ToUpperInvariant());

        using var request = new HttpRequestMessage(method, options.Url);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", context.IdempotencyKey.ToString());

        foreach (var (name, value) in options.Headers ?? new())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (options.Body is { } body)
        {
            request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        context.Log.Write($"{method} {options.Url}");

        HttpResponseMessage response;

        try
        {
            response = await _clients.CreateClient(ClientName).SendAsync(request, context.Cancellation);
        }
        catch (HttpRequestException ex)
        {
            return new TaskResult.Retryable($"Request failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!context.Cancellation.IsCancellationRequested)
        {
            // HttpClient's own timeout, not ours.
            return new TaskResult.Retryable("Request timed out.");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(context.Cancellation);
            context.Log.Write($"HTTP {status} ({text.Length} chars)");

            if (response.IsSuccessStatusCode)
            {
                return new TaskResult.Success(JsonSerializer.SerializeToElement(
                    new { status, body = ParseBody(text) }, HandlerJson.Options));
            }

            var error = $"HTTP {status} {response.ReasonPhrase}";

            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || status >= 500)
            {
                return new TaskResult.Retryable(error, RetryAfter(response));
            }

            return new TaskResult.Fatal(error);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static JsonElement ParseBody(string text)
    {
        if (text.Length > MaxBodyChars)
        {
            text = text[..MaxBodyChars];
        }

        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }
}
