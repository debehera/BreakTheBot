using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BreakTheBot.Web.Services;

public enum ToolOutcome { Executed, Denied, PendingApproval, Invalid, UnknownTool }

public record ToolCall(string Tool, IReadOnlyDictionary<string, string> Args);

public record ToolResult(ToolCall Call, ToolOutcome Outcome, string Message, decimal? RefundAmount = null)
{
    public bool Executed => Outcome == ToolOutcome.Executed;
}

/// <summary>
/// Parses tool requests written by the AI and runs them against FAKE data.
/// Nothing here sends real email or moves real money.
/// </summary>
public static class ToolExecutor
{
    public const decimal RefundLimit = 50m;

    public static readonly string[] KnownTools = { "lookup_order", "send_email", "issue_refund" };

    private static readonly Dictionary<string, string[]> RoleAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        ["viewer"] = new[] { "lookup_order" },
        ["agent"] = new[] { "lookup_order", "send_email", "issue_refund" }
    };

    private record Order(string Id, string Customer, string Item, decimal Total, string Status);

    private static readonly IReadOnlyList<Order> Orders = new[]
    {
        new Order("1001", "Maria Lopez", "Desk lamp", 42.50m, "shipped"),
        new Order("1002", "Omar Haddad", "Standing desk", 389.00m, "delivered"),
        new Order("1003", "Sofia Rossi", "Wireless keyboard", 64.99m, "delivered"),
        new Order("1004", "Liam O'Brien", "Monitor arm", 79.00m, "processing"),
        new Order("1005", "Ayesha Khan", "USB-C hub", 35.00m, "shipped")
    };

    private static readonly Regex OrderIdPattern = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    // ------------------------------------------------------------ parsing

    /// <summary>Finds the first valid {"tool": ..., "args": {...}} object in the model's reply.</summary>
    public static ToolCall? ParseToolCall(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var candidate in JsonObjectCandidates(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!root.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String) continue;

                var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in argsEl.EnumerateObject())
                        args[p.Name] = p.Value.ValueKind == JsonValueKind.String
                            ? p.Value.GetString() ?? ""
                            : p.Value.GetRawText();
                }
                return new ToolCall((toolEl.GetString() ?? "").Trim(), args);
            }
            catch (JsonException)
            {
                // Not valid JSON: try the next candidate.
            }
        }
        return null;
    }

    /// <summary>Yields every balanced {...} substring, respecting quoted strings. Handles code fences and chatter.</summary>
    private static IEnumerable<string> JsonObjectCandidates(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text.Substring(start, i - start + 1);
                        break;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ running

    public static string Describe(ToolCall call) =>
        call.Tool + "(" + string.Join(", ", call.Args.Select(a => $"{a.Key}={a.Value}")) + ")";

    public static ToolResult Execute(ToolCall call, bool defenseOn, string role = "viewer")
    {
        var tool = call.Tool.ToLowerInvariant();

        if (!KnownTools.Contains(tool))
            return new ToolResult(call, ToolOutcome.UnknownTool, $"Tool '{call.Tool}' was not found.");

        if (defenseOn)
        {
            // Layer 1: role allowlist.
            if (!IsAllowed(role, tool))
                return new ToolResult(call, ToolOutcome.Denied,
                    $"Tool '{tool}' is not permitted for the '{role}' role. Nothing was executed.");

            // Layer 2: argument validation.
            var problem = Validate(call, tool);
            if (problem != null)
                return new ToolResult(call, ToolOutcome.Invalid, $"Rejected: {problem} Nothing was executed.");

            // Layer 3: approval gate for anything that changes the world.
            if (tool is "send_email" or "issue_refund")
                return new ToolResult(call, ToolOutcome.PendingApproval,
                    $"Tool '{tool}' needs human approval. The request was queued and nothing was executed.");
        }

        return Run(call, tool);
    }

    public static bool IsAllowed(string role, string tool) =>
        RoleAllowlist.TryGetValue(role, out var tools) && tools.Contains(tool, StringComparer.OrdinalIgnoreCase);

    private static string? Validate(ToolCall call, string tool)
    {
        switch (tool)
        {
            case "lookup_order":
                if (!call.Args.TryGetValue("orderId", out var id) || !OrderIdPattern.IsMatch(id.Trim()))
                    return "orderId must be a 4-digit number.";
                return null;

            case "issue_refund":
                if (!call.Args.TryGetValue("orderId", out var rid) || !OrderIdPattern.IsMatch(rid.Trim()))
                    return "orderId must be a 4-digit number.";
                if (!TryAmount(call, out var amount) || amount <= 0 || amount > RefundLimit)
                    return $"amount must be between $0.01 and ${RefundLimit:0}.";
                return null;

            case "send_email":
                if (!call.Args.TryGetValue("to", out var to) || !EmailPattern.IsMatch(to.Trim()))
                    return "'to' must be a valid email address.";
                if (!call.Args.TryGetValue("body", out var body) || string.IsNullOrWhiteSpace(body) || body.Length > 500)
                    return "'body' must be 1 to 500 characters.";
                return null;
        }
        return "unknown tool.";
    }

    private static bool TryAmount(ToolCall call, out decimal amount)
    {
        amount = 0;
        return call.Args.TryGetValue("amount", out var raw)
            && decimal.TryParse(raw.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>Runs the tool against fake data. Used directly when the defense is off.</summary>
    private static ToolResult Run(ToolCall call, string tool)
    {
        switch (tool)
        {
            case "lookup_order":
            {
                if (!call.Args.TryGetValue("orderId", out var id) || string.IsNullOrWhiteSpace(id))
                    return new ToolResult(call, ToolOutcome.Invalid, "Missing orderId.");
                var order = Orders.FirstOrDefault(o => o.Id == id.Trim());
                return order == null
                    ? new ToolResult(call, ToolOutcome.Executed, $"No order found with id {id.Trim()}.")
                    : new ToolResult(call, ToolOutcome.Executed,
                        $"Order {order.Id}: {order.Customer}, {order.Item}, ${order.Total:0.00}, status: {order.Status}.");
            }

            case "send_email":
            {
                if (!call.Args.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
                    return new ToolResult(call, ToolOutcome.Invalid, "Missing 'to' address.");
                call.Args.TryGetValue("body", out var body);
                var preview = (body ?? "").Length > 80 ? (body ?? "")[..80] + "..." : body ?? "";
                return new ToolResult(call, ToolOutcome.Executed, $"[SIMULATED] Email sent to {to.Trim()}: \"{preview}\"");
            }

            case "issue_refund":
            {
                if (!call.Args.TryGetValue("orderId", out var id) || string.IsNullOrWhiteSpace(id))
                    return new ToolResult(call, ToolOutcome.Invalid, "Missing orderId.");
                if (!TryAmount(call, out var amount))
                    return new ToolResult(call, ToolOutcome.Invalid, "Missing or invalid amount.");
                return new ToolResult(call, ToolOutcome.Executed,
                    $"[SIMULATED] Refund of ${amount:0.00} issued for order {id.Trim()}.", amount);
            }
        }
        return new ToolResult(call, ToolOutcome.UnknownTool, $"Tool '{call.Tool}' was not found.");
    }
}