using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class SpeechPlanPersistenceModelTests
{
    [Fact]
    public void Npgsql_model_has_bounded_speech_plan_storage_and_owner_series_scoped_keys()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var draft = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ChapterSpeechPlanDraft)));
        var draftSegment = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(SpeechSegmentDraft)));
        var revision = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ConfirmedSpeechPlanRevision)));
        var confirmedSegment = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(ConfirmedSpeechSegment)));
        var jobPlan = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(NarrationJobSpeechPlan)));

        AssertProperty(draft, nameof(ChapterSpeechPlanDraft.SourceHash), 128, nullable: false);
        AssertProperty(draftSegment, nameof(SpeechSegmentDraft.TextHash), 128, nullable: false);
        AssertProperty(revision, nameof(ConfirmedSpeechPlanRevision.Fingerprint), 64, nullable: false);
        AssertProperty(confirmedSegment, nameof(ConfirmedSpeechSegment.TextHash), 128, nullable: false);

        Assert.Contains(
            draft.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "Id" }));
        Assert.Contains(
            revision.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "Id" }));
        Assert.Contains(
            draft.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "BookId", "ChapterId" }));
        Assert.Contains(
            draftSegment.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "PlanDraftId", "SortOrder" }));
        Assert.Contains(
            revision.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "BookId", "ChapterId", "RevisionNumber" }));
        Assert.Contains(
            jobPlan.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "NarrationJobId", "ChapterSortOrder" }));
        Assert.Contains(
            jobPlan.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "OwnerId", "SeriesId", "NarrationJobId", "ConfirmedSpeechPlanRevisionId" }));
    }

    [Fact]
    public void Add_speech_plan_revisions_migration_matches_model_and_locks_narrator_segments_to_no_character()
    {
        using var db = CreateContext();
        Assert.False(db.Database.HasPendingModelChanges());

        var script = db.GetService<IMigrator>().GenerateScript(
            fromMigration: null,
            toMigration: null,
            MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE chapter_speech_plan_drafts", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE speech_segment_drafts", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE confirmed_speech_plan_revisions", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE confirmed_speech_segments", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE narration_job_speech_plans", script, StringComparison.Ordinal);
        Assert.Contains("CK_speech_segment_drafts_narrator_no_character", script, StringComparison.Ordinal);
        Assert.Contains("CK_confirmed_speech_segments_narrator_no_character", script, StringComparison.Ordinal);
        Assert.Contains("CK_speech_segment_drafts_inner_monologue_state", script, StringComparison.Ordinal);
        Assert.Contains("CK_confirmed_speech_segments_inner_monologue_state", script, StringComparison.Ordinal);
        Assert.Contains("FK_speech_plan_drafts_books_OwnerId_BookId", script, StringComparison.Ordinal);
        Assert.Contains("FK_speech_plan_drafts_chapters_BookId_ChapterId", script, StringComparison.Ordinal);
        Assert.Contains("FK_njob_speech_plans_job_scope", script, StringComparison.Ordinal);
        // NarrationJob.SeriesId must stay nullable — SingleVoice jobs never carry a series, and
        // the CK_njobs_correlations constraint from Task 0 requires that. Regressing this would
        // corrupt every existing single-voice narration job.
        Assert.DoesNotContain("ALTER COLUMN \"SeriesId\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn", script, StringComparison.Ordinal);
    }

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_speech_plan_model;Username=storyvoice;Password=unused")
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static void AssertProperty(
        IEntityType entity,
        string propertyName,
        int maximumLength,
        bool nullable)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(propertyName));
        Assert.Equal(maximumLength, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }
}
