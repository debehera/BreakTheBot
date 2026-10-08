using BreakTheBot.Web.Services;

namespace BreakTheBot.Tests;

/// <summary>A scripted stand-in for Gemini: returns queued replies and records every request.</summary>
public class FakeLlmClient : ILlmClient
{
    private readonly Queue<LlmResult> _queue = new();

    public List<LlmRequest> Requests { get; } = new();
    public LlmResult Default { get; set; } = new(true, "ok", 10, 5);

    public void Enqueue(LlmResult result) => _queue.Enqueue(result);

    public void EnqueueText(string text, int input = 10, int output = 5) =>
        _queue.Enqueue(new LlmResult(true, text, input, output));

    public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : Default);
    }
}