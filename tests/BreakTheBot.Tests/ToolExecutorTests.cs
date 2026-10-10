using BreakTheBot.Web.Services;

namespace BreakTheBot.Tests;

public class ToolExecutorTests
{
    private static ToolCall Call(string tool, params (string Key, string Value)[] args) =>
        new(tool, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase));

    // ------------------------------------------------------------ parsing

    [Fact]
    public void Parses_a_plain_json_tool_request()
    {
        var call = ToolExecutor.ParseToolCall("""{"tool":"lookup_order","args":{"orderId":"1002"}}""");

        Assert.NotNull(call);
        Assert.Equal("lookup_order", call!.Tool);
        Assert.Equal("1002", call.Args["orderId"]);
    }

    [Fact]
    public void Parses_json_wrapped_in_a_code_fence()
    {
        var text = "```json\n" + """{"tool":"lookup_order","args":{"orderId":"1002"}}""" + "\n```";

        var call = ToolExecutor.ParseToolCall(text);

        Assert.Equal("lookup_order", call?.Tool);
    }

    [Fact]
    public void Parses_json_surrounded_by_chatter()
    {
        var text = "Sure, one moment! " + """{"tool":"lookup_order","args":{"orderId":"1001"}}""" + " Done.";

        Assert.Equal("1001", ToolExecutor.ParseToolCall(text)?.Args["orderId"]);
    }

    [Fact]
    public void Skips_json_objects_that_are_not_tool_requests()
    {
        var text = """{"note":"hello"} {"tool":"lookup_order","args":{"orderId":"1003"}}""";

        Assert.Equal("lookup_order", ToolExecutor.ParseToolCall(text)?.Tool);
    }

    [Fact]
    public void Numeric_arguments_are_kept_as_text()
    {
        var call = ToolExecutor.ParseToolCall("""{"tool":"issue_refund","args":{"orderId":"1002","amount":389}}""");

        Assert.Equal("389", call!.Args["amount"]);
    }

    [Fact]
    public void Tool_names_are_trimmed_and_argument_names_ignore_case()
    {
        var call = ToolExecutor.ParseToolCall("""{"tool":" issue_refund ","args":{"OrderId":"1002","Amount":"25"}}""");

        Assert.Equal("issue_refund", call!.Tool);
        Assert.Equal("1002", call.Args["orderid"]);
        Assert.Equal("25", call.Args["AMOUNT"]);
    }

    [Fact]
    public void A_request_without_args_gets_an_empty_argument_list()
    {
        var call = ToolExecutor.ParseToolCall("""{"tool":"lookup_order"}""");

        Assert.NotNull(call);
        Assert.Empty(call!.Args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Just a normal sentence.")]
    [InlineData("{not valid json}")]
    [InlineData("{\"hello\":\"world\"}")]
    [InlineData("{\"tool\": 5}")]
    [InlineData("{\"tool\":\"lookup_order\"")]
    public void Garbage_is_ignored_and_never_throws(string? text)
    {
        Assert.Null(ToolExecutor.ParseToolCall(text));
    }

    [Fact]
    public void Describe_shows_the_tool_and_its_arguments()
    {
        var call = Call("lookup_order", ("orderId", "1002"));

        Assert.Equal("lookup_order(orderId=1002)", ToolExecutor.Describe(call));
    }

    // ------------------------------------------------------------ defense OFF (everything runs)

    [Fact]
    public void Without_the_defense_lookup_returns_the_fake_order()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "1002")), defenseOn: false);

        Assert.True(result.Executed);
        Assert.Contains("Omar Haddad", result.Message);
        Assert.Contains("Standing desk", result.Message);
    }

    [Fact]
    public void Without_the_defense_an_unknown_order_id_is_reported_not_crashed()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "9999")), defenseOn: false);

        Assert.True(result.Executed);
        Assert.Contains("No order found", result.Message);
    }

    [Fact]
    public void Without_the_defense_a_viewer_can_issue_any_refund()
    {
        var result = ToolExecutor.Execute(
            Call("issue_refund", ("orderId", "1002"), ("amount", "389")), defenseOn: false, role: "viewer");

        Assert.True(result.Executed);
        Assert.Equal(389m, result.RefundAmount);
        Assert.Contains("[SIMULATED]", result.Message);
    }

    [Fact]
    public void Without_the_defense_email_is_simulated_too()
    {
        var result = ToolExecutor.Execute(
            Call("send_email", ("to", "maria@example.com"), ("body", "Your order shipped.")), defenseOn: false);

        Assert.True(result.Executed);
        Assert.Contains("[SIMULATED] Email sent to maria@example.com", result.Message);
    }

    [Fact]
    public void Without_the_defense_a_refund_with_no_amount_is_invalid_not_a_crash()
    {
        var result = ToolExecutor.Execute(Call("issue_refund", ("orderId", "1002")), defenseOn: false);

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_tools_are_never_executed(bool defenseOn)
    {
        var result = ToolExecutor.Execute(Call("delete_everything"), defenseOn);

        Assert.Equal(ToolOutcome.UnknownTool, result.Outcome);
        Assert.False(result.Executed);
    }

    [Fact]
    public void Tool_names_are_matched_without_regard_to_case()
    {
        var result = ToolExecutor.Execute(Call("Lookup_Order", ("orderId", "1001")), defenseOn: false);

        Assert.True(result.Executed);
    }

    // ------------------------------------------------------------ defense ON: layer 1 (role allowlist)

    [Theory]
    [InlineData("issue_refund")]
    [InlineData("send_email")]
    public void A_viewer_cannot_use_the_dangerous_tools(string tool)
    {
        var result = ToolExecutor.Execute(
            Call(tool, ("orderId", "1002"), ("amount", "10"), ("to", "a@b.com"), ("body", "hi")),
            defenseOn: true, role: "viewer");

        Assert.Equal(ToolOutcome.Denied, result.Outcome);
        Assert.Contains("not permitted", result.Message);
        Assert.Null(result.RefundAmount);
    }

    [Fact]
    public void A_viewer_can_still_look_up_orders()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "1003")), defenseOn: true, role: "viewer");

        Assert.True(result.Executed);
        Assert.Contains("Sofia Rossi", result.Message);
    }

    [Fact]
    public void An_unknown_role_gets_nothing()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "1003")), defenseOn: true, role: "hacker");

        Assert.Equal(ToolOutcome.Denied, result.Outcome);
    }

    [Fact]
    public void Role_names_ignore_case()
    {
        Assert.True(ToolExecutor.IsAllowed("VIEWER", "lookup_order"));
        Assert.False(ToolExecutor.IsAllowed("VIEWER", "issue_refund"));
        Assert.True(ToolExecutor.IsAllowed("agent", "issue_refund"));
    }

    // ------------------------------------------------------------ defense ON: layer 2 (validation)

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abcd")]
    [InlineData("10 02")]
    [InlineData("")]
    public void Order_ids_must_be_four_digits(string orderId)
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", orderId)), defenseOn: true, role: "viewer");

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void A_well_formed_but_unknown_order_id_passes_validation()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "9999")), defenseOn: true, role: "viewer");

        Assert.True(result.Executed);
        Assert.Contains("No order found", result.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("50.01")]
    [InlineData("389")]
    [InlineData("abc")]
    [InlineData("")]
    public void Refund_amounts_outside_the_limit_are_rejected(string amount)
    {
        var result = ToolExecutor.Execute(
            Call("issue_refund", ("orderId", "1002"), ("amount", amount)), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
        Assert.Null(result.RefundAmount);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("25")]
    [InlineData("$25")]
    [InlineData("50")]
    public void Refund_amounts_inside_the_limit_pass_validation_but_still_need_approval(string amount)
    {
        var result = ToolExecutor.Execute(
            Call("issue_refund", ("orderId", "1002"), ("amount", amount)), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.PendingApproval, result.Outcome);
        Assert.False(result.Executed);
    }

    [Fact]
    public void Refunds_need_a_valid_order_id()
    {
        var result = ToolExecutor.Execute(
            Call("issue_refund", ("orderId", "oops"), ("amount", "10")), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
    }

    [Theory]
    [InlineData("not-an-email", "hello")]
    [InlineData("", "hello")]
    [InlineData("a@b.com", "")]
    [InlineData("a@b.com", "   ")]
    public void Emails_need_a_valid_address_and_a_body(string to, string body)
    {
        var result = ToolExecutor.Execute(
            Call("send_email", ("to", to), ("body", body)), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Email_bodies_are_limited_to_500_characters()
    {
        var result = ToolExecutor.Execute(
            Call("send_email", ("to", "a@b.com"), ("body", new string('x', 501))), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Argument_names_from_the_parser_work_with_validation()
    {
        var call = ToolExecutor.ParseToolCall("""{"tool":"issue_refund","args":{"OrderId":"1002","Amount":"25"}}""")!;

        var result = ToolExecutor.Execute(call, defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.PendingApproval, result.Outcome);
    }

    // ------------------------------------------------------------ defense ON: layer 3 (approval gate)

    [Fact]
    public void A_valid_email_from_an_allowed_role_is_queued_not_sent()
    {
        var result = ToolExecutor.Execute(
            Call("send_email", ("to", "maria@example.com"), ("body", "Your order shipped.")), defenseOn: true, role: "agent");

        Assert.Equal(ToolOutcome.PendingApproval, result.Outcome);
        Assert.Contains("needs human approval", result.Message);
        Assert.DoesNotContain("Email sent", result.Message);
    }

    [Fact]
    public void Lookups_never_need_approval()
    {
        var result = ToolExecutor.Execute(Call("lookup_order", ("orderId", "1001")), defenseOn: true, role: "agent");

        Assert.True(result.Executed);
    }
}