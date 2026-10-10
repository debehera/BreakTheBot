using System.Net;
using System.Text;
using System.Text.Json;

namespace BreakTheBot.Web.Services;

public class GeminiClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiClient> _log;

    public GeminiClient(HttpClient http, IConfiguration config, ILogger<GeminiClient> log)
    {
        _http = http;
        _log = log;
        _apiKey = config["Gemini:ApiKey"] ?? "";
        _model = config["Gemini:Model"] ?? "gemini-3.5-flash-lite";
    }

    public async Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return Fail("The AI is not configured on the server.");

        // Conversation: earlier turns, then the new user message.
        var contents = new List<object>();
        foreach (var turn in request.History)
            contents.Add(new { role = turn.Role, parts = new[] { new { text = turn.Text } } });
        contents.Add(new { role = "user", parts = new[] { new { text = request.UserMessage } } });

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents,
            generationConfig = new
            {
                maxOutputTokens = request.MaxOutputTokens,
                temperature = request.Temperature
            }
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{_model}:generateContent");
            message.Headers.Add("x-goog-api-key", _apiKey); // key goes in a header, never in the URL or logs
            message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(message, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _log.LogWarning("Gemini returned 429 (rate limited).");
                return new LlmResult(false, "", 0, 0, "The AI is busy right now. Try again in a minute.", RateLimited: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Gemini returned HTTP {Status}.", (int)response.StatusCode);
                return Fail("The AI service had a problem. Please try again shortly.");
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            return Parse(raw);
        }
        catch (TaskCanceledException)
        {
            _log.LogWarning("Gemini request timed out.");
            return Fail("The AI took too long to answer. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("Gemini request failed: {Message}", ex.Message);
            return Fail("Could not reach the AI service. Please try again.");
        }
        catch (JsonException)
        {
            _log.LogWarning("Gemini returned unreadable JSON.");
            return Fail("The AI returned an unreadable answer. Please try again.");
        }
    }

    private LlmResult Parse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        int input = 0, output = 0;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            input = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
            output = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        }

        // The prompt itself was blocked by Google's safety filters.
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
            return new LlmResult(false, "", input, output, "The AI declined to answer that. Try rewording it.", SafetyBlocked: true);

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return Fail("The AI returned no answer. Please try again.");

        var candidate = candidates[0];
        var text = new StringBuilder();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                {
                    text.Append(t.GetString());
                }
                else if (part.TryGetProperty("functionCall", out var fc))
                {
                    // The model used its native function-calling format. Convert it into the
                    // plain JSON tool request that the rest of the app understands.
                    var name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var args = fc.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                    text.Append("{\"tool\":").Append(JsonSerializer.Serialize(name))
                        .Append(",\"args\":").Append(args).Append('}');
                }
            }
        }

        var finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;

        if (text.Length == 0)
        {
            if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII")
                return new LlmResult(false, "", input, output, "The AI declined to answer that. Try rewording it.", SafetyBlocked: true);
            _log.LogWarning("Gemini returned no text. finishReason={Finish}, outputTokens={Output}", finish, output);
            return Fail("The AI returned an empty answer. Please try again.");
        }

        return new LlmResult(true, text.ToString(), input, output);
    }

    private static LlmResult Fail(string error) => new(false, "", 0, 0, error);
}