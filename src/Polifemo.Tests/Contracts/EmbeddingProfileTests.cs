using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Profiles;

namespace Polifemo.Tests.Contracts;

public class EmbeddingProfileTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static EmbeddingProfile Profile(
        string model = "nomic-embed-text-v1.5", string revision = Sha, string documentPrefix = "search_document: ",
        string queryPrefix = "search_query: ", int maxTokens = 8192, int dimension = 768, bool normalize = true) =>
        new(new EmbeddingModelRef("local", model, revision), new EmbeddingPrefixes(documentPrefix, queryPrefix), "nomic-bert", maxTokens, dimension, "mean", normalize);

    [Fact]
    public void Profile_id_known_answer()
    {
        var p = new EmbeddingProfile(new EmbeddingModelRef("local", "nomic-embed-text-v1.5", new string('a', 40)), new EmbeddingPrefixes("search_document: ", "search_query: "), "nomic-bert", 8192, 768, "mean", true);
        Assert.Equal("4dce704324ebb2075e9c8fac814a217e8125103d0f7801152a8ec0990db9b209", p.ProfileId);
    }

    [Fact]
    public void Profile_id_is_stable_and_every_field_matters()
    {
        var id = Profile().ProfileId;
        Assert.Equal(id, Profile().ProfileId);

        var changed = new[]
        {
            Profile(model: "other"), Profile(revision: new string('b', 40)), Profile(documentPrefix: "doc: "),
            Profile(queryPrefix: "q: "), Profile(maxTokens: 512), Profile(dimension: 384), Profile(normalize: false),
            new EmbeddingProfile(new EmbeddingModelRef("remote", "nomic-embed-text-v1.5", Sha), new EmbeddingPrefixes("search_document: ", "search_query: "), "nomic-bert", 8192, 768, "mean", true),
            new EmbeddingProfile(new EmbeddingModelRef("local", "nomic-embed-text-v1.5", Sha), new EmbeddingPrefixes("search_document: ", "search_query: "), "other", 8192, 768, "mean", true),
            new EmbeddingProfile(new EmbeddingModelRef("local", "nomic-embed-text-v1.5", Sha), new EmbeddingPrefixes("search_document: ", "search_query: "), "nomic-bert", 8192, 768, "cls", true),
        };
        Assert.All(changed, p => Assert.NotEqual(id, p.ProfileId));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("master")]
    [InlineData("latest")]
    [InlineData("v1.5")]
    [InlineData("")]
    [InlineData("0123456789abcdef")]
    public void Revision_must_be_an_immutable_commit_sha(string revision) =>
        Assert.Throws<ArgumentException>(() => Profile(revision: revision));

    [Theory]
    [InlineData(0, 768)]
    [InlineData(-1, 768)]
    [InlineData(8192, 0)]
    [InlineData(8192, -5)]
    public void Max_tokens_and_dimension_must_be_positive(int maxTokens, int dimension) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile(maxTokens: maxTokens, dimension: dimension));

    [Fact]
    public void Same_profile_is_the_same_space()
    {
        var exception = Record.Exception(() => EmbeddingProfile.EnsureSameSpace(Profile(), Profile()));

        Assert.Null(exception);
    }

    [Fact]
    public void Different_profiles_are_rejected_with_EMB_PROFILE_MISMATCH_and_both_ids()
    {
        var query = Profile(model: "other-model");
        var index = Profile();

        var ex = Assert.Throws<EmbeddingProfileMismatchException>(() => EmbeddingProfile.EnsureSameSpace(query, index));

        Assert.Equal("EMB_PROFILE_MISMATCH", ex.Code);
        Assert.Equal(index.ProfileId, ex.Expected);
        Assert.Equal(query.ProfileId, ex.Actual);
    }

    [Fact]
    public void A_same_model_with_a_different_normalization_is_a_different_space()
    {
        Assert.Throws<EmbeddingProfileMismatchException>(() => EmbeddingProfile.EnsureSameSpace(Profile(normalize: false), Profile()));
    }

    // data-model.md, ProfileVersions: which version feeds which hash.
    private static ProfileVersions Versions(
        string extractor = "x1", string chunker = "c1", string mapping = "m1", string profile = "p1", string template = "t1") =>
        new(extractor, chunker, mapping, profile, template);

    private static (ChunkHash Chunk, EmbeddingInputHash Embedding, LogicalKey Key, FileHash File) Derive(ProfileVersions v)
    {
        var key = LogicalKey.Create("repo", "src/A.cs", "A::M", "method", "void M()");
        var chunk = ChunkHash.Create(v.ChunkerVersion, v.MappingVersion, key, new { text = "body" });
        var embedding = EmbeddingInputHash.Create(v.EmbeddingProfileId, v.InputTemplateVersion, "document", "body"u8);
        return (chunk, embedding, key, FileHash.Create("source"u8));
    }

    [Fact]
    public void Changing_one_version_changes_only_the_hashes_that_depend_on_it()
    {
        var baseline = Derive(Versions());

        var extractor = Derive(Versions(extractor: "x2"));
        Assert.Equal(baseline, extractor);

        var chunker = Derive(Versions(chunker: "c2"));
        Assert.NotEqual(baseline.Chunk, chunker.Chunk);
        Assert.Equal((baseline.Embedding, baseline.Key, baseline.File), (chunker.Embedding, chunker.Key, chunker.File));

        var mapping = Derive(Versions(mapping: "m2"));
        Assert.NotEqual(baseline.Chunk, mapping.Chunk);
        Assert.Equal((baseline.Embedding, baseline.Key, baseline.File), (mapping.Embedding, mapping.Key, mapping.File));

        var profile = Derive(Versions(profile: "p2"));
        Assert.NotEqual(baseline.Embedding, profile.Embedding);
        Assert.Equal((baseline.Chunk, baseline.Key, baseline.File), (profile.Chunk, profile.Key, profile.File));

        var template = Derive(Versions(template: "t2"));
        Assert.NotEqual(baseline.Embedding, template.Embedding);
        Assert.Equal((baseline.Chunk, baseline.Key, baseline.File), (template.Chunk, template.Key, template.File));
    }
}
