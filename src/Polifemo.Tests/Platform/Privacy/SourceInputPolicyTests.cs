using System.Text;
using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Identity;
using Polifemo.Platform.Privacy;

namespace Polifemo.Tests.Platform.Privacy;

/// <summary>F02-03: excluded and secret-bearing inputs follow an explicit, reported action before indexing or inference.</summary>
public sealed partial class SourceInputPolicyTests
{
    [System.Text.RegularExpressions.GeneratedRegex(@"\.cs$")]
    private static partial System.Text.RegularExpressions.Regex CSharpFile();

    // Built at run time so this file does not look like a leaked credential to a scanner.
    private static readonly string AwsKey = "AKIA" + new string('A', 8) + "1234567" + "8";
    private static readonly string GitHubToken = "ghp_" + new string('x', 36);
    private static readonly string PrivateKeyHeader = "-----BEGIN RSA " + "PRIVATE KEY-----";

    private static InputDecision Evaluate(SourceInputPolicy policy, string path, string content) =>
        policy.Evaluate(path, Encoding.UTF8.GetBytes(content));

    [Theory]
    [InlineData(".env")]
    [InlineData("src/.env.production")]
    [InlineData("certs/server.pem")]
    [InlineData("certs/client.PFX")]
    [InlineData("home/id_rsa")]
    [InlineData("deploy/credentials")]
    [InlineData("secrets.json")]
    [InlineData(".git/config")]
    [InlineData(".tokensave/tokensave.db")]
    public void Secret_bearing_paths_are_excluded_whatever_their_content(string path)
    {
        var decision = Evaluate(SourceInputPolicy.Default, path, "harmless");

        Assert.Equal(InputAction.Exclude, decision.Action);
        Assert.Single(decision.RuleIds);
        Assert.Null(decision.Content);
    }

    [Fact]
    public void Ordinary_source_is_included_unchanged()
    {
        var decision = Evaluate(SourceInputPolicy.Default, "src/App.cs", "class App { string password; }");

        Assert.Equal(InputAction.Include, decision.Action);
        Assert.Empty(decision.RuleIds);
        Assert.Null(decision.Content);
    }

    [Fact]
    public void Binary_files_are_excluded()
    {
        var decision = SourceInputPolicy.Default.Evaluate("lib/native.dat", [1, 2, 0, 3]);

        Assert.Equal(InputAction.Exclude, decision.Action);
        Assert.Equal(["binary-file"], decision.RuleIds);
    }

    [Theory]
    [InlineData(SecretRules.PrivateKey)]
    [InlineData(SecretRules.AwsKey)]
    [InlineData(SecretRules.GitHub)]
    public void Key_material_inside_a_file_excludes_the_whole_file(string rule)
    {
        var content = rule switch
        {
            SecretRules.PrivateKey => $"x\n{PrivateKeyHeader}\nabc",
            SecretRules.AwsKey => $"var k = \"{AwsKey}\";",
            _ => $"token {GitHubToken}",
        };

        var decision = Evaluate(SourceInputPolicy.Default, "src/Config.cs", content);

        Assert.Equal(InputAction.Exclude, decision.Action);
        Assert.Equal([rule], decision.RuleIds);
        Assert.Null(decision.Content);
    }

    [Fact]
    public void Loose_secret_assignments_are_redacted_visibly_and_the_decision_says_so()
    {
        const string original = "var connection = \"Server=db;Password=hunter2secret;\";\nvar password = \"correct-horse\";\nint answer = 42;";

        var decision = Evaluate(SourceInputPolicy.Default, "src/Db.cs", original);

        Assert.Equal(InputAction.Redact, decision.Action);
        Assert.Contains(SecretRules.Assignment, decision.RuleIds);
        Assert.Contains(SecretRules.ConnectionString, decision.RuleIds);
        Assert.DoesNotContain("hunter2secret", decision.Content);
        Assert.DoesNotContain("correct-horse", decision.Content);
        Assert.Contains("[REDACTED:secret-assignment]", decision.Content);
        Assert.Contains("int answer = 42;", decision.Content);
        Assert.Equal(FileHash.Create(Encoding.UTF8.GetBytes(decision.Content!)), decision.ContentHash);
        Assert.NotEqual(FileHash.Create(Encoding.UTF8.GetBytes(original)), decision.ContentHash);
    }

    [Fact]
    public void The_most_severe_action_wins()
    {
        var decision = Evaluate(SourceInputPolicy.Default, "src/Mixed.cs", $"var p = \"correct-horse\"; // password: \"x1234\"\n{AwsKey}");

        Assert.Equal(InputAction.Exclude, decision.Action);
        Assert.Equal([SecretRules.AwsKey], decision.RuleIds);
    }

    [Fact]
    public void A_policy_can_reject_and_a_rejection_fails_the_run_without_the_secret()
    {
        var strict = new SourceInputPolicy([], new Dictionary<string, InputAction> { [SecretRules.AwsKey] = InputAction.Reject });

        var decision = Evaluate(strict, "src/Config.cs", $"k={AwsKey}");
        var ex = Assert.Throws<PrivacyPolicyException>(() => decision.ThrowIfRejected());

        Assert.Equal(InputAction.Reject, decision.Action);
        Assert.Equal("INPUT_REJECTED", ex.Code);
        Assert.Contains("src/Config.cs", ex.Message);
        Assert.Contains(SecretRules.AwsKey, ex.Message);
        Assert.DoesNotContain(AwsKey, ex.Message);
    }

    [Fact]
    public void A_rule_set_to_include_does_not_fire() =>
        Assert.Equal(InputAction.Include,
            Evaluate(new SourceInputPolicy([], new Dictionary<string, InputAction> { [SecretRules.AwsKey] = InputAction.Include }), "a.cs", AwsKey).Action);

    [Fact]
    public void An_include_path_rule_does_not_bypass_secret_screening()
    {
        var policy = new SourceInputPolicy(
            [new PathRule("sources", CSharpFile(), InputAction.Include)],
            new Dictionary<string, InputAction> { [SecretRules.AwsKey] = InputAction.Exclude });

        Assert.Equal(InputAction.Exclude, Evaluate(policy, "a.cs", $"k={AwsKey}").Action);
        Assert.Equal(InputAction.Include, Evaluate(policy, "a.cs", "class A {}").Action);
    }

    [Fact]
    public void A_quoted_secret_with_an_apostrophe_or_spaces_is_redacted_whole()
    {
        var decision = Evaluate(SourceInputPolicy.Default, "a.cs", "var x = 1; password = \"correct'horse staple\"; var y = 2;");

        Assert.Equal(InputAction.Redact, decision.Action);
        Assert.DoesNotContain("horse", decision.Content);
        Assert.DoesNotContain("staple", decision.Content);
        Assert.Contains("var y = 2;", decision.Content);
    }

    [Fact]
    public void An_included_decision_does_not_throw()
    {
        var decision = Evaluate(SourceInputPolicy.Default, "a.cs", "x");

        Assert.Same(decision, decision.ThrowIfRejected());
    }
}
