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
    public void Compatibility_migration_precedes_phase_b_and_generates_reversible_postgresql_sql()
    {
        using var db = CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();
        var compatibilityIndex = Array.FindIndex(
            migrations,
            migration => migration.EndsWith("_AddNarrationModeCompatibility", StringComparison.Ordinal));

        var seriesCastIndex = Array.FindIndex(
            migrations,
            migration => migration.EndsWith("_AddSeriesCast", StringComparison.Ordinal));
        var castRebuildIndex = Array.FindIndex(
            migrations,
            migration => migration.EndsWith("_AddCastRebuildPersistence", StringComparison.Ordinal));

        Assert.True(compatibilityIndex > 0);
        Assert.True(seriesCastIndex > compatibilityIndex);
        Assert.True(castRebuildIndex > seriesCastIndex);

        var previousMigration = migrations[compatibilityIndex - 1];
        var compatibilityMigration = migrations[compatibilityIndex];
        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(previousMigration, compatibilityMigration);
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

        var down = migrator.GenerateScript(compatibilityMigration, previousMigration);
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