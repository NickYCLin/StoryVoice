using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class NarrationModeMigrationTests
{
    [Fact]
    public void Npgsql_model_has_the_phase_a_mode_contract()
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NarrationJob));
        Assert.NotNull(entity);

        var mode = entity.FindProperty(nameof(NarrationJob.Mode));
        Assert.NotNull(mode);
        Assert.False(mode.IsNullable);
        Assert.Equal(30, mode.GetMaxLength());
        Assert.Equal(typeof(string), mode.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(NarrationMode.SingleVoice.ToString(), mode.GetDefaultValue()?.ToString());

        var check = entity.GetCheckConstraints()
            .Single(constraint => constraint.Name == "CK_narration_jobs_mode");
        Assert.Equal("\"Mode\" IN ('SingleVoice', 'MultiCharacter')", check.Sql);

        var singleVoiceIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(NarrationJob.OwnerId),
                    nameof(NarrationJob.BookId),
                    nameof(NarrationJob.ContentBookId),
                    nameof(NarrationJob.SourceHash),
                    nameof(NarrationJob.Voice),
                    nameof(NarrationJob.Rate)
                }));
        Assert.True(singleVoiceIndex.IsUnique);
        Assert.Equal("\"Mode\" = 'SingleVoice'", singleVoiceIndex.GetFilter());
    }

    [Fact]
    public void Compatibility_migration_is_newest_and_generates_reversible_postgresql_sql()
    {
        using var db = CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();

        Assert.EndsWith("_AddNarrationModeCompatibility", migrations[^1], StringComparison.Ordinal);

        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(migrations[^2], migrations[^1]);
        Assert.Contains(
            "ADD \"Mode\" character varying(30) NOT NULL DEFAULT 'SingleVoice'",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE narration_jobs SET \"Mode\" = 'SingleVoice'",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (\"Mode\" IN ('SingleVoice', 'MultiCharacter'))",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE \"Mode\" = 'SingleVoice'",
            up,
            StringComparison.Ordinal);

        var down = migrator.GenerateScript(migrations[^1], migrations[^2]);
        Assert.Contains("WHERE \"Mode\" IS DISTINCT FROM 'SingleVoice'", down, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION 'Cannot roll back narration Mode while non-SingleVoice jobs exist.'", down, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT \"CK_narration_jobs_mode\"", down, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN \"Mode\"", down, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE \"Mode\" = 'SingleVoice'", down, StringComparison.Ordinal);
    }

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_model_contract;Username=storyvoice;Password=unused")
            .Options;
        return new StoryVoiceDbContext(options);
    }
}