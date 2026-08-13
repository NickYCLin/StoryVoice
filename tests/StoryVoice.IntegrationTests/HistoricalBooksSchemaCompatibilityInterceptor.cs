using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StoryVoice.IntegrationTests;

/// <summary>
/// Lets migration tests exercise an intentionally historical migration boundary with the current
/// CLR model. Production always reaches the latest migration before accepting writes; these tests
/// deliberately stop early to inspect previous migration contracts.
/// </summary>
internal sealed class HistoricalBooksSchemaCompatibilityInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context?.Database.IsNpgsql() == true)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                DO $compat$
                BEGIN
                    IF to_regclass('public.books') IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = 'public'
                                AND table_name = 'books'
                                AND column_name = 'IsArchived')
                    THEN
                        ALTER TABLE books
                        ADD COLUMN "IsArchived" boolean NOT NULL DEFAULT FALSE;
                    END IF;
                    IF to_regclass('public.series_characters') IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = 'public'
                                AND table_name = 'series_characters'
                                AND column_name = 'CharacterProfileId')
                    THEN
                        ALTER TABLE series_characters
                        ADD COLUMN "CharacterProfileId" uuid NULL;
                    END IF;
                    IF to_regclass('public.story_series') IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = 'public'
                                AND table_name = 'story_series'
                                AND column_name = 'PointOfViewCharacterId')
                    THEN
                        ALTER TABLE story_series
                        ADD COLUMN "PointOfViewCharacterId" uuid NULL;
                    END IF;
                END
                $compat$;
                """,
                cancellationToken);
        }

        return result;
    }
}
