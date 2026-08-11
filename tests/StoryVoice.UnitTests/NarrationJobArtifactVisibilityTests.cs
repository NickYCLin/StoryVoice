using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.UnitTests;

public sealed class NarrationJobArtifactVisibilityTests
{
    private const string MultiCharacterStagedFactoryName = "CreateMultiCharacterStaged";

    private static readonly Type[] MultiCharacterStagedFactoryParameterTypes =
    [
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(Guid),
        typeof(string),
        typeof(DateTimeOffset)
    ];

    [Fact]
    public void Artifact_visibility_enum_has_the_exact_public_contract()
    {
        Assert.True(typeof(NarrationArtifactVisibility).IsPublic);
        Assert.True(typeof(NarrationArtifactVisibility).IsEnum);
        Assert.Equal(
            ["Staged", "Published", "Historical"],
            Enum.GetNames<NarrationArtifactVisibility>());
        Assert.Equal(
            [0, 1, 2],
            Enum.GetValues<NarrationArtifactVisibility>().Select(value => (int)value));
    }

    [Fact]
    public void Single_voice_factory_remains_published_and_has_no_multi_character_correlations()
    {
        var ownerId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var contentBookId = Guid.NewGuid();
        var rightsAttestedAt = DateTimeOffset.UtcNow;

        var job = NarrationJob.Create(
            ownerId,
            bookId,
            contentBookId,
            "synthetic-source-hash",
            "synthetic-single-voice",
            "synthetic-rate",
            rightsAttestedAt);

        Assert.Equal(ownerId, job.OwnerId);
        Assert.Equal(bookId, job.BookId);
        Assert.Equal(contentBookId, job.ContentBookId);
        Assert.Equal(NarrationMode.SingleVoice, job.Mode);
        Assert.Equal(NarrationArtifactVisibility.Published, job.Visibility);
        Assert.Equal(NarrationJobStatus.Queued, job.Status);
        Assert.Equal(0, job.ProgressPercent);
        Assert.Equal(0, job.Attempts);
        Assert.Equal("synthetic-single-voice", job.Voice);
        Assert.Equal("synthetic-rate", job.Rate);
        Assert.Equal(rightsAttestedAt, job.RightsAttestedAt);
        Assert.Null(job.SeriesId);
        Assert.Null(job.CastRevisionId);
        Assert.Null(job.SpeechPlanRevisionId);
        Assert.Null(job.RebuildBatchId);
        Assert.Null(job.RebuildMemberId);
        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Fact]
    public void Multi_character_factory_creates_a_staged_queued_job_with_locked_correlations()
    {
        var input = MultiCharacterInput.Create();

        var job = input.CreateJob();

        Assert.Equal(input.OwnerId, job.OwnerId);
        Assert.Equal(input.BookId, job.BookId);
        Assert.Equal(input.ContentBookId, job.ContentBookId);
        Assert.Equal(input.SeriesId, job.SeriesId);
        Assert.Equal(input.CastRevisionId, job.CastRevisionId);
        Assert.Equal(input.SpeechPlanRevisionId, job.SpeechPlanRevisionId);
        Assert.Equal(input.RebuildBatchId, job.RebuildBatchId);
        Assert.Equal(input.RebuildMemberId, job.RebuildMemberId);
        Assert.Equal(input.SourceHash, job.SourceHash);
        Assert.Equal(input.RightsAttestedAt, job.RightsAttestedAt);
        Assert.Equal(NarrationMode.MultiCharacter, job.Mode);
        Assert.Equal(NarrationArtifactVisibility.Staged, job.Visibility);
        Assert.Equal(NarrationJobStatus.Queued, job.Status);
        Assert.Equal(0, job.ProgressPercent);
        Assert.Equal(0, job.Attempts);
        Assert.False(job.CancellationRequested);
        Assert.Equal(job.CreatedAt, job.UpdatedAt);
        Assert.Equal(job.CreatedAt, job.NextAttemptAt);
        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Multi_character_factory_rejects_every_empty_identifier(int emptyIdentifierIndex)
    {
        var input = MultiCharacterInput.Create();
        var identifiers = new[]
        {
            input.OwnerId,
            input.BookId,
            input.ContentBookId,
            input.SeriesId,
            input.CastRevisionId,
            input.SpeechPlanRevisionId,
            input.RebuildBatchId,
            input.RebuildMemberId
        };
        identifiers[emptyIdentifierIndex] = Guid.Empty;

        Assert.Throws<ArgumentException>(() => InvokeMultiCharacterStagedFactory(
            identifiers[0],
            identifiers[1],
            identifiers[2],
            identifiers[3],
            identifiers[4],
            identifiers[5],
            identifiers[6],
            identifiers[7],
            input.SourceHash,
            input.RightsAttestedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Multi_character_factory_rejects_missing_source_hash(string? sourceHash)
    {
        var input = MultiCharacterInput.Create();

        Assert.Throws<ArgumentException>(() => input.CreateJob(sourceHash!));
    }

    [Fact]
    public void Multi_character_factory_uses_the_existing_source_hash_limit()
    {
        var input = MultiCharacterInput.Create();
        var maximumSourceHash = new string('a', 128);

        var job = input.CreateJob(maximumSourceHash);

        Assert.Equal(maximumSourceHash, job.SourceHash);
        Assert.Throws<ArgumentException>(() => input.CreateJob(new string('a', 129)));
    }

    [Fact]
    public void Multi_character_staged_factory_is_internal_with_the_exact_signature()
    {
        Assert.DoesNotContain(
            typeof(NarrationJob).GetMethods(BindingFlags.Static | BindingFlags.Public),
            method => method.Name == MultiCharacterStagedFactoryName);

        var factory = GetMultiCharacterStagedFactory();

        Assert.True(factory.IsAssembly);
        Assert.Equal(typeof(NarrationJob), factory.ReturnType);
        Assert.Equal(
            MultiCharacterStagedFactoryParameterTypes,
            factory.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Multi_character_voice_and_rate_are_explicit_internal_compatibility_markers_not_factory_claims()
    {
        var voiceMarker = AssertCompatibilityMarker(
            "MultiCharacterVoiceCompatibilityMarker",
            "speech-plan");
        var rateMarker = AssertCompatibilityMarker(
            "MultiCharacterRateCompatibilityMarker",
            "per-segment");
        var factory = GetMultiCharacterStagedFactory();
        Assert.DoesNotContain(
            factory.GetParameters(),
            parameter => parameter.Name is "voice" or "rate" or "provider");

        var job = MultiCharacterInput.Create().CreateJob();

        Assert.Equal(voiceMarker, job.Voice);
        Assert.Equal(rateMarker, job.Rate);
        Assert.Equal(NarrationMode.MultiCharacter, job.Mode);
    }

    [Fact]
    public void Correlations_are_getter_only_and_publication_mutation_is_narrowly_internal()
    {
        var correlationProperties = new[]
        {
            nameof(NarrationJob.SeriesId),
            nameof(NarrationJob.CastRevisionId),
            nameof(NarrationJob.SpeechPlanRevisionId),
            nameof(NarrationJob.RebuildBatchId),
            nameof(NarrationJob.RebuildMemberId)
        };
        Assert.All(
            correlationProperties,
            propertyName => Assert.Null(typeof(NarrationJob)
                .GetProperty(propertyName)!
                .GetSetMethod(nonPublic: true)));

        var visibilityProperty = typeof(NarrationJob).GetProperty(nameof(NarrationJob.Visibility));
        Assert.NotNull(visibilityProperty);
        Assert.Null(visibilityProperty.GetSetMethod(nonPublic: false));
        AssertInternalMethod("Publish");
        AssertInternalMethod("MarkHistorical");
        Assert.DoesNotContain(
            typeof(NarrationJob).GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name is "Publish" or "MarkHistorical");

        var friendAssemblies = typeof(NarrationJob).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();
        Assert.Contains("StoryVoice.Infrastructure", friendAssemblies);
        Assert.DoesNotContain("StoryVoice.UnitTests", friendAssemblies);
    }

    [Fact]
    public void Regular_playback_requires_published_completed_safe_positive_audio_metadata()
    {
        var queued = CreateSingleVoiceJob();
        var running = CreateSingleVoiceJob();
        Claim(running);
        var failed = CreateSingleVoiceJob();
        Claim(failed);
        failed.FailPermanently("synthetic_failure");
        var cancelled = CreateSingleVoiceJob();
        cancelled.Cancel();
        var completed = CompleteSingleVoiceJob();
        var stagedCompleted = CompleteMultiCharacterJob();
        var publishedCompleted = CompleteMultiCharacterJob();
        InvokeInternal(publishedCompleted, "Publish", publishedCompleted.UpdatedAt);
        var historicalCompleted = CompleteMultiCharacterJob();
        InvokeInternal(historicalCompleted, "Publish", historicalCompleted.UpdatedAt);
        InvokeInternal(historicalCompleted, "MarkHistorical", historicalCompleted.UpdatedAt);

        Assert.False(queued.IsAvailableForRegularPlayback);
        Assert.False(running.IsAvailableForRegularPlayback);
        Assert.False(failed.IsAvailableForRegularPlayback);
        Assert.False(cancelled.IsAvailableForRegularPlayback);
        Assert.True(completed.IsAvailableForRegularPlayback);
        Assert.False(stagedCompleted.IsAvailableForRegularPlayback);
        Assert.True(publishedCompleted.IsAvailableForRegularPlayback);
        Assert.False(historicalCompleted.IsAvailableForRegularPlayback);
    }

    [Fact]
    public void Regular_playback_rejects_completed_rows_with_missing_unsafe_or_non_positive_audio_metadata()
    {
        var invalidMetadata = new (string? Path, long? Bytes)[]
        {
            (null, 42),
            (string.Empty, 42),
            ("../outside.mp3", 42),
            ("folder/./audio.mp3", 42),
            ("/absolute/audio.mp3", 42),
            ("safe/audio.mp3", null),
            ("safe/audio.mp3", 0)
        };

        foreach (var metadata in invalidMetadata)
        {
            var job = CompleteSingleVoiceJob();
            SetProperty(job, nameof(NarrationJob.AudioRelativePath), metadata.Path);
            SetProperty(job, nameof(NarrationJob.AudioBytes), metadata.Bytes);

            Assert.False(job.IsAvailableForRegularPlayback);
        }
    }

    [Theory]
    [InlineData(@"C:\outside.mp3")]
    [InlineData("C:/outside.mp3")]
    public void Complete_rejects_windows_drive_paths_atomically_on_every_host(string unsafePath)
    {
        var job = CreateSingleVoiceJob();
        Claim(job);

        AssertRejectedAtomically<ArgumentException>(
            job,
            () => job.Complete(unsafePath, 512));

        Assert.Equal(NarrationJobStatus.Running, job.Status);
        Assert.Null(job.AudioRelativePath);
        Assert.Null(job.AudioBytes);
    }

    [Theory]
    [InlineData(@"C:\outside.mp3")]
    [InlineData("C:/outside.mp3")]
    public void Regular_playback_rejects_corrupt_completed_rows_with_windows_drive_paths(string unsafePath)
    {
        var job = CompleteSingleVoiceJob();
        SetProperty(job, nameof(NarrationJob.AudioRelativePath), unsafePath);

        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Fact]
    public void Completing_multi_character_job_keeps_the_artifact_staged()
    {
        var job = MultiCharacterInput.Create().CreateJob();
        Claim(job);

        job.Complete("synthetic/audio.mp3", 512);

        Assert.Equal(NarrationJobStatus.Completed, job.Status);
        Assert.Equal(NarrationArtifactVisibility.Staged, job.Visibility);
        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Fact]
    public void Multi_character_artifact_can_move_staged_to_published_to_historical_at_equal_timestamps()
    {
        var job = CompleteMultiCharacterJob();
        var correlations = CaptureCorrelations(job);
        var completedStamp = job.ConcurrencyStamp;
        var publishedAt = job.UpdatedAt;

        InvokeInternal(job, "Publish", publishedAt);

        Assert.Equal(NarrationArtifactVisibility.Published, job.Visibility);
        Assert.Equal(publishedAt, job.UpdatedAt);
        Assert.NotEqual(completedStamp, job.ConcurrencyStamp);
        Assert.True(job.IsAvailableForRegularPlayback);
        Assert.Equal(correlations, CaptureCorrelations(job));
        var publishedStamp = job.ConcurrencyStamp;

        InvokeInternal(job, "MarkHistorical", job.UpdatedAt);

        Assert.Equal(NarrationArtifactVisibility.Historical, job.Visibility);
        Assert.Equal(publishedAt, job.UpdatedAt);
        Assert.NotEqual(publishedStamp, job.ConcurrencyStamp);
        Assert.False(job.IsAvailableForRegularPlayback);
        Assert.Equal(correlations, CaptureCorrelations(job));
    }

    [Fact]
    public void Completed_single_voice_artifact_can_move_from_published_to_historical()
    {
        var job = CompleteSingleVoiceJob();
        var completedStamp = job.ConcurrencyStamp;

        InvokeInternal(job, "MarkHistorical", job.UpdatedAt);

        Assert.Equal(NarrationMode.SingleVoice, job.Mode);
        Assert.Equal(NarrationArtifactVisibility.Historical, job.Visibility);
        Assert.NotEqual(completedStamp, job.ConcurrencyStamp);
        Assert.False(job.IsAvailableForRegularPlayback);
        Assert.Null(job.SeriesId);
        Assert.Null(job.CastRevisionId);
        Assert.Null(job.SpeechPlanRevisionId);
        Assert.Null(job.RebuildBatchId);
        Assert.Null(job.RebuildMemberId);
    }

    [Fact]
    public void Publication_and_history_reject_time_regression_atomically()
    {
        var staged = CompleteMultiCharacterJob();
        AssertRejectedAtomically<ArgumentOutOfRangeException>(
            staged,
            () => InvokeInternal(staged, "Publish", staged.UpdatedAt.AddTicks(-1)));

        var published = CompleteMultiCharacterJob();
        InvokeInternal(published, "Publish", published.UpdatedAt);
        AssertRejectedAtomically<ArgumentOutOfRangeException>(
            published,
            () => InvokeInternal(published, "MarkHistorical", published.UpdatedAt.AddTicks(-1)));
    }

    [Fact]
    public void Publish_rejects_all_ineligible_mode_status_visibility_and_metadata_rows_atomically()
    {
        var singleVoice = CompleteSingleVoiceJob();
        var queued = MultiCharacterInput.Create().CreateJob();
        var running = MultiCharacterInput.Create().CreateJob();
        Claim(running);
        var failed = MultiCharacterInput.Create().CreateJob();
        Claim(failed);
        failed.FailPermanently("synthetic_failure");
        var cancelled = MultiCharacterInput.Create().CreateJob();
        cancelled.Cancel();
        var alreadyPublished = CompleteMultiCharacterJob();
        InvokeInternal(alreadyPublished, "Publish", alreadyPublished.UpdatedAt);
        var historical = CompleteMultiCharacterJob();
        InvokeInternal(historical, "Publish", historical.UpdatedAt);
        InvokeInternal(historical, "MarkHistorical", historical.UpdatedAt);
        var missingAudio = CompleteMultiCharacterJob();
        SetProperty(missingAudio, nameof(NarrationJob.AudioRelativePath), null);

        foreach (var job in new[]
                 {
                     singleVoice,
                     queued,
                     running,
                     failed,
                     cancelled,
                     alreadyPublished,
                     historical,
                     missingAudio
                 })
        {
            AssertRejectedAtomically<InvalidOperationException>(
                job,
                () => InvokeInternal(job, "Publish", job.UpdatedAt));
        }
    }

    [Fact]
    public void Mark_historical_rejects_all_ineligible_visibility_status_and_metadata_rows_atomically()
    {
        var staged = CompleteMultiCharacterJob();
        var queued = CreateSingleVoiceJob();
        var running = CreateSingleVoiceJob();
        Claim(running);
        var failed = CreateSingleVoiceJob();
        Claim(failed);
        failed.FailPermanently("synthetic_failure");
        var cancelled = CreateSingleVoiceJob();
        cancelled.Cancel();
        var missingAudio = CompleteSingleVoiceJob();
        SetProperty(missingAudio, nameof(NarrationJob.AudioBytes), null);
        var historical = CompleteSingleVoiceJob();
        InvokeInternal(historical, "MarkHistorical", historical.UpdatedAt);

        foreach (var job in new[]
                 {
                     staged,
                     queued,
                     running,
                     failed,
                     cancelled,
                     missingAudio,
                     historical
                 })
        {
            AssertRejectedAtomically<InvalidOperationException>(
                job,
                () => InvokeInternal(job, "MarkHistorical", job.UpdatedAt));
        }
    }

    [Theory]
    [InlineData(NarrationJobStatus.Failed)]
    [InlineData(NarrationJobStatus.Cancelled)]
    public void Requeue_preserves_published_single_voice_visibility_and_empty_correlations(
        NarrationJobStatus terminalStatus)
    {
        var job = CreateSingleVoiceJob();
        MoveToTerminalStatus(job, terminalStatus);
        var previousStamp = job.ConcurrencyStamp;

        job.Requeue();

        Assert.Equal(NarrationJobStatus.Queued, job.Status);
        Assert.Equal(NarrationArtifactVisibility.Published, job.Visibility);
        Assert.NotEqual(previousStamp, job.ConcurrencyStamp);
        Assert.Null(job.SeriesId);
        Assert.Null(job.CastRevisionId);
        Assert.Null(job.SpeechPlanRevisionId);
        Assert.Null(job.RebuildBatchId);
        Assert.Null(job.RebuildMemberId);
        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Theory]
    [InlineData(NarrationJobStatus.Failed)]
    [InlineData(NarrationJobStatus.Cancelled)]
    public void Requeue_preserves_staged_multi_character_visibility_and_locked_correlations(
        NarrationJobStatus terminalStatus)
    {
        var job = MultiCharacterInput.Create().CreateJob();
        var correlations = CaptureCorrelations(job);
        MoveToTerminalStatus(job, terminalStatus);
        var previousStamp = job.ConcurrencyStamp;

        job.Requeue();

        Assert.Equal(NarrationJobStatus.Queued, job.Status);
        Assert.Equal(NarrationArtifactVisibility.Staged, job.Visibility);
        Assert.Equal(correlations, CaptureCorrelations(job));
        Assert.NotEqual(previousStamp, job.ConcurrencyStamp);
        Assert.False(job.IsAvailableForRegularPlayback);
    }

    [Fact]
    public void Historical_completed_job_rejects_every_mutating_transition_atomically()
    {
        var job = CompleteMultiCharacterJob();
        InvokeInternal(job, "Publish", job.UpdatedAt);
        InvokeInternal(job, "MarkHistorical", job.UpdatedAt);

        var transitions = new Action[]
        {
            () => job.Claim("synthetic-worker", job.UpdatedAt.AddMinutes(5), job.UpdatedAt),
            () => job.Complete("synthetic/replacement.mp3", 1_024),
            () => job.FailOrRequeue("synthetic_failure", maxAttempts: 3, failedAt: job.UpdatedAt),
            () => job.FailPermanently("synthetic_failure"),
            () => job.Cancel(),
            () => job.Requeue(),
            () => InvokeInternal(job, "Publish", job.UpdatedAt),
            () => InvokeInternal(job, "MarkHistorical", job.UpdatedAt)
        };

        foreach (var transition in transitions)
        {
            AssertRejectedAtomically<InvalidOperationException>(job, transition);
        }
    }

    [Fact]
    public void Request_cancellation_on_historical_completed_job_is_a_snapshot_preserving_no_op()
    {
        var job = CompleteMultiCharacterJob();
        InvokeInternal(job, "Publish", job.UpdatedAt);
        InvokeInternal(job, "MarkHistorical", job.UpdatedAt);
        var before = CaptureObservableSnapshot(job);

        job.RequestCancellation();

        AssertObservableSnapshot(before, job);
    }

    private static NarrationJob CreateSingleVoiceJob() =>
        NarrationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "synthetic-source-hash",
            "synthetic-single-voice",
            "synthetic-rate",
            DateTimeOffset.UtcNow);

    private static NarrationJob CompleteSingleVoiceJob()
    {
        var job = CreateSingleVoiceJob();
        Claim(job);
        job.Complete("synthetic/audio.mp3", 512);
        return job;
    }

    private static NarrationJob CompleteMultiCharacterJob()
    {
        var job = MultiCharacterInput.Create().CreateJob();
        Claim(job);
        job.Complete("synthetic/audio.mp3", 512);
        return job;
    }

    private static void Claim(NarrationJob job) =>
        job.Claim("synthetic-worker", job.UpdatedAt.AddMinutes(5), job.UpdatedAt);

    private static void MoveToTerminalStatus(NarrationJob job, NarrationJobStatus terminalStatus)
    {
        if (terminalStatus == NarrationJobStatus.Cancelled)
        {
            job.Cancel();
            return;
        }

        Claim(job);
        job.FailPermanently("synthetic_failure");
        Assert.Equal(NarrationJobStatus.Failed, terminalStatus);
    }

    private static string AssertCompatibilityMarker(string fieldName, string expectedValue)
    {
        var field = typeof(NarrationJob).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.True(field.IsAssembly);
        Assert.True(field.IsLiteral);
        Assert.False(field.IsInitOnly);
        var value = Assert.IsType<string>(field.GetRawConstantValue());
        Assert.Equal(expectedValue, value);
        return value;
    }

    private static MethodInfo GetMultiCharacterStagedFactory()
    {
        var method = typeof(NarrationJob).GetMethod(
            MultiCharacterStagedFactoryName,
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: MultiCharacterStagedFactoryParameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method.IsAssembly, $"{MultiCharacterStagedFactoryName} must remain narrowly internal.");
        return method;
    }

    private static NarrationJob InvokeMultiCharacterStagedFactory(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        Guid seriesId,
        Guid castRevisionId,
        Guid speechPlanRevisionId,
        Guid rebuildBatchId,
        Guid rebuildMemberId,
        string sourceHash,
        DateTimeOffset rightsAttestedAt)
    {
        var method = GetMultiCharacterStagedFactory();
        try
        {
            return Assert.IsType<NarrationJob>(method.Invoke(
                null,
                [
                    ownerId,
                    bookId,
                    contentBookId,
                    seriesId,
                    castRevisionId,
                    speechPlanRevisionId,
                    rebuildBatchId,
                    rebuildMemberId,
                    sourceHash,
                    rightsAttestedAt
                ]));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void AssertInternalMethod(string methodName)
    {
        var method = typeof(NarrationJob).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method.IsAssembly, $"{methodName} must remain narrowly internal.");
        Assert.Equal([typeof(DateTimeOffset)], method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void InvokeInternal(
        NarrationJob job,
        string methodName,
        DateTimeOffset now)
    {
        var method = typeof(NarrationJob).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method.IsAssembly, $"{methodName} must remain narrowly internal.");
        try
        {
            method.Invoke(job, [now]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void SetProperty(NarrationJob job, string propertyName, object? value)
    {
        var property = typeof(NarrationJob).GetProperty(propertyName);
        Assert.NotNull(property);
        var setter = property.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        setter.Invoke(job, [value]);
    }

    private static void AssertRejectedAtomically<TException>(NarrationJob job, Action transition)
        where TException : Exception
    {
        var before = CaptureObservableSnapshot(job);

        Assert.Throws<TException>(transition);

        AssertObservableSnapshot(before, job);
    }

    private static void AssertObservableSnapshot(
        SortedDictionary<string, object?> before,
        NarrationJob job)
    {
        var after = CaptureObservableSnapshot(job);
        Assert.Equal(before.Keys, after.Keys);
        foreach (var property in before)
        {
            Assert.Equal(property.Value, after[property.Key]);
        }
    }

    private static SortedDictionary<string, object?> CaptureObservableSnapshot(NarrationJob job) =>
        new(
            typeof(NarrationJob)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(property => property.Name, property => property.GetValue(job)),
            StringComparer.Ordinal);

    private static (Guid? SeriesId, Guid? CastRevisionId, Guid? SpeechPlanRevisionId, Guid? RebuildBatchId, Guid? RebuildMemberId)
        CaptureCorrelations(NarrationJob job) =>
        (job.SeriesId, job.CastRevisionId, job.SpeechPlanRevisionId, job.RebuildBatchId, job.RebuildMemberId);

    private sealed record MultiCharacterInput(
        Guid OwnerId,
        Guid BookId,
        Guid ContentBookId,
        Guid SeriesId,
        Guid CastRevisionId,
        Guid SpeechPlanRevisionId,
        Guid RebuildBatchId,
        Guid RebuildMemberId,
        string SourceHash,
        DateTimeOffset RightsAttestedAt)
    {
        public static MultiCharacterInput Create() =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "synthetic-source-hash",
                DateTimeOffset.UtcNow);

        public NarrationJob CreateJob() => CreateJob(SourceHash);

        public NarrationJob CreateJob(string sourceHash) =>
            InvokeMultiCharacterStagedFactory(
                OwnerId,
                BookId,
                ContentBookId,
                SeriesId,
                CastRevisionId,
                SpeechPlanRevisionId,
                RebuildBatchId,
                RebuildMemberId,
                sourceHash,
                RightsAttestedAt);
    }
}
