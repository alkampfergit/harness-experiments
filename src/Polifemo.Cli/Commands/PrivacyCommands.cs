using System.CommandLine;
using Polifemo.Platform.Privacy;

namespace Polifemo.Cli;

public static class PrivacyCommands
{
    public static Command Create(TextWriter stdout, TextWriter stderr)
    {
        var privacy = new Command("privacy", "Verify the local-only policy.");
        privacy.Subcommands.Add(Check(stdout, stderr));
        privacy.Subcommands.Add(VerifyModel(stdout, stderr));
        return privacy;
    }

    private static Command Check(TextWriter stdout, TextWriter stderr)
    {
        var worktree = new Option<string?>("--worktree") { Description = "Worktree whose TokenSave upload setting is checked." };
        var command = new Command("check", "Validate the configured endpoints, the offline environment and TokenSave uploads.") { worktree };
        command.SetAction((parse, _) => CliOutput.RunAsync(stdout, stderr, () =>
        {
            var settings = LocalOnlySettings.FromEnvironment();
            var dir = parse.GetValue(worktree);
            if (dir is not null) TokenSaveUploads.EnsureDisabled(dir);
            var offlineMissing = OfflineEnvironment.Missing();
            return Task.FromResult((offlineMissing.Count == 0 ? ExitCodes.Ok : ExitCodes.CheckFailed, (object)new
            {
                ok = offlineMissing.Count == 0,
                endpoints = new
                {
                    store = settings.Store.GetLeftPart(UriPartial.Authority),
                    embedding = settings.Embedding.GetLeftPart(UriPartial.Authority),
                    reranking = settings.Reranking.GetLeftPart(UriPartial.Authority),
                },
                allowedServiceHosts = settings.Policy.ServiceHosts,
                offlineVariablesMissing = offlineMissing,
                tokensaveUploadsDisabled = dir is null ? (bool?)null : true,
            }));
        }));
        return command;
    }

    private static Command VerifyModel(TextWriter stdout, TextWriter stderr)
    {
        var model = new Option<string>("--model") { Description = "Model name, e.g. org/name.", Required = true };
        var revision = new Option<string>("--revision") { Description = "40-character commit SHA.", Required = true };
        var reviews = new Option<string>("--reviews") { Description = "JSON file of model reviews.", Required = true };
        var modelDir = new Option<string?>("--model-dir") { Description = $"Defaults to {LocalOnlySettings.ModelDirectoryVariable}." };
        var command = new Command("verify-model", "Check that a pinned model is reviewed and present; never downloads.") { model, revision, reviews, modelDir };
        command.SetAction((parse, _) => CliOutput.RunAsync(stdout, stderr, () =>
        {
            var dir = parse.GetValue(modelDir) ?? LocalOnlySettings.FromEnvironment().ModelDirectory;
            var path = ModelArtifacts.Require(parse.GetValue(model)!, parse.GetValue(revision)!, dir, ModelArtifacts.LoadReviews(parse.GetValue(reviews)!));
            return Task.FromResult((ExitCodes.Ok, (object)new { ok = true, artifactDirectory = path }));
        }));
        return command;
    }
}
