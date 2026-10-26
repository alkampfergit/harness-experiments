using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;

namespace Polifemo.Tests.Platform.Privacy;

/// <summary>F02-03: logs, traces and error output keep identifiers and causes but never payload.</summary>
public sealed class DiagnosticsTests
{
    private static readonly string Token = "ghp_" + new string('q', 36);

    [Fact]
    public void An_event_keeps_identifiers_and_scrubs_urls_and_secrets()
    {
        var e = DiagnosticEvent.Create("ES_BULK_FAILED", "publisher", new Dictionary<string, string>
        {
            ["snapshot_id"] = "abc123",
            ["path"] = "src/App.cs",
            ["detail"] = $"failed at https://user:pw@private.example/index?q=secret with Bearer abcdefghijklmnop1234 and {Token}",
        });

        var json = e.ToJson();

        Assert.Contains("abc123", json);
        Assert.Contains("src/App.cs", json);
        Assert.DoesNotContain("private.example", json);
        Assert.DoesNotContain("secret", json.Replace("redacted", ""));
        Assert.DoesNotContain(Token, json);
        Assert.DoesNotContain("abcdefghijklmnop1234", json);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("source_text")]
    [InlineData("Content")]
    [InlineData("request_body")]
    [InlineData("endpoint_url")]
    [InlineData("password")]
    [InlineData("auth_token")]
    [InlineData("snippet")]
    public void Payload_bearing_field_names_are_refused(string name) =>
        Assert.Throws<ArgumentException>(() => DiagnosticEvent.Create("X", "c", new Dictionary<string, string> { [name] = "v" }));

    [Fact]
    public void Values_are_bounded_and_single_line()
    {
        var e = DiagnosticEvent.Create("X", "c", new Dictionary<string, string> { ["stage"] = "a\nb" + new string('z', 500) });

        Assert.DoesNotContain('\n', e.Fields["stage"]);
        Assert.True(e.Fields["stage"].Length <= 201);
    }

    [Fact]
    public void A_typed_failure_keeps_its_code_cause_and_identifiers()
    {
        var ex = new SearchStoreException("ES_UNREACHABLE", "Cannot reach the store.", new HttpRequestException("boom"));

        var e = DiagnosticEvent.FromException("publisher", ex, new Dictionary<string, string> { ["snapshot_id"] = "s1" });

        Assert.Equal("ES_UNREACHABLE", e.Code);
        Assert.Equal("SearchStoreException", e.Fields["exception_type"]);
        Assert.Equal("HttpRequestException", e.Fields["cause_type"]);
        Assert.Equal("s1", e.Fields["snapshot_id"]);
        Assert.Equal("Cannot reach the store.", e.Fields["detail"]);
    }

    [Fact]
    public void An_unexpected_exception_message_is_dropped_because_it_may_echo_input()
    {
        var e = DiagnosticEvent.FromException("chunker", new FormatException("bad token near: class Secret { }"));

        Assert.Equal("UNEXPECTED", e.Code);
        Assert.False(e.Fields.ContainsKey("detail"));
        Assert.DoesNotContain("Secret", e.ToJson());
    }

    [Theory]
    [InlineData("GIT_FAILED")]
    [InlineData("PREP_INDEX_FAILED")]
    public async Task Subprocess_failures_never_carry_their_message_to_diagnostics_or_cli_output(string code)
    {
        const string Leak = "fatal: bad object class SecretService { string Query = \"salary report\"; }";
        var ex = new FixturePreparationException(code, Leak);

        var e = DiagnosticEvent.FromException("prep", ex);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        await CliOutput.RunAsync(stdout, stderr, () => throw ex);

        Assert.Equal(code, e.Code);
        Assert.Equal("FixturePreparationException", e.Fields["exception_type"]);
        foreach (var text in new[] { e.ToJson(), stdout.ToString(), stderr.ToString() })
        {
            Assert.Contains(code, text);
            Assert.DoesNotContain("SecretService", text);
            Assert.DoesNotContain("salary", text);
        }
    }

    [Fact]
    public async Task The_git_wrapper_message_holds_the_exit_code_and_no_stderr()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(
            () => Polifemo.Platform.Fixtures.Git.RunAsync(null, ["no-such-subcommand-zz"], null, TestContext.Current.CancellationToken));

        Assert.Equal("GIT_FAILED", ex.Code);
        Assert.Contains("exit code", ex.Message);
        Assert.DoesNotContain("no-such-subcommand-zz'", ex.Message);
    }

    [Theory]
    [InlineData("before -----BEGIN RSA PRIVATE KEY-----\nMIIabc\ndef\n-----END RSA PRIVATE KEY----- after", "before [redacted:private-key-block] after")]
    [InlineData("log: -----BEGIN PRIVATE KEY-----\nMIIabc\nnever terminated", "log: [redacted:private-key-block]")]
    public void A_private_key_is_redacted_through_its_end_marker_or_to_the_end_of_the_message(string input, string expected) =>
        Assert.Equal(expected, Scrubber.Scrub(input));

    [Fact]
    public async Task Cli_error_output_is_scrubbed_for_typed_failures()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliOutput.RunAsync(stdout, stderr, () =>
            throw new SearchStoreException("ES_UNREACHABLE", $"Cannot reach https://elastic:pw@db.private.example:9200/x?key=1 using {Token}"));

        Assert.Equal(3, exit);
        foreach (var output in new[] { stdout.ToString(), stderr.ToString() })
        {
            Assert.Contains("ES_UNREACHABLE", output);
            Assert.DoesNotContain("private.example", output);
            Assert.DoesNotContain("elastic:pw", output);
            Assert.DoesNotContain(Token, output);
        }
        using var doc = JsonDocument.Parse(stdout.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Cli_error_output_drops_the_message_of_untyped_failures()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        await CliOutput.RunAsync(stdout, stderr, () => throw new InvalidOperationException("query was: where is the payroll code"));

        Assert.DoesNotContain("payroll", stdout.ToString() + stderr);
        Assert.Contains("UNEXPECTED", stderr.ToString());
    }

    [Fact]
    public void Scrubbing_is_deterministic_and_leaves_plain_text_alone()
    {
        Assert.Equal("nothing to hide", Scrubber.Scrub("nothing to hide"));
        Assert.Equal(Scrubber.Scrub("see https://a.example/x"), Scrubber.Scrub("see https://a.example/x"));
    }

    [Fact]
    public void Privacy_failures_map_to_a_failed_check_exit_code()
    {
        Assert.Equal(ExitCodes.CheckFailed, ExitCodes.For(new PrivacyPolicyException("POLICY_EXTERNAL_ENDPOINT", "x")));
        Assert.Equal(ExitCodes.CheckFailed, ExitCodes.For(new ScopeException("SCOPE_NOT_AUTHORIZED", "x")));
    }
}
