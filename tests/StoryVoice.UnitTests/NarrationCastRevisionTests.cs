using System.Reflection;
using System.Runtime.ExceptionServices;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.UnitTests;

public sealed class NarrationCastRevisionTests
{
    private const int MaximumProviderLength = 50;
    private const int MaximumVoiceLength = 200;
    private const int MaximumParameterLength = 50;
    private const int MaximumCharacterNameLength = 200;

    [Fact]
    public void Creation_captures_an_immutable_draft_snapshot_with_a_lowercase_sha256_fingerprint()
    {
        var fixture = CreateFixture();

        var revision = fixture.Revision.Create();

        Assert.Equal(fixture.Revision.Id, revision.Id);
        Assert.Equal(fixture.Revision.OwnerId, revision.OwnerId);
        Assert.Equal(fixture.Revision.SeriesId, revision.SeriesId);
        Assert.Equal(7, revision.RevisionNumber);
        Assert.Equal(NarrationCastRevisionStatus.Draft, revision.Status);
        Assert.Null(revision.EpochNumber);
        Assert.Null(revision.ActivatedAt);
        Assert.False(revision.CanBeUsedByRegularJob);
        Assert.Equal(fixture.Revision.CreatedAt, revision.CreatedAt);
        Assert.Equal("provider-main", revision.NarratorProvider);
        Assert.Equal("provider-main-v1", revision.NarratorProviderVersion);
        Assert.Equal("narrator-voice", revision.NarratorVoice);
        Assert.Equal("rate-default", revision.NarratorRate);
        Assert.Equal("pitch-default", revision.NarratorPitch);
        Assert.Equal("volume-default", revision.NarratorVolume);
        Assert.Equal(350, revision.DefaultSpeakerPauseMs);
        Assert.Equal(900, revision.ChapterPauseMs);
        Assert.Equal("composition-v1", revision.CompositionVersion);
        Assert.Equal("audio-profile-v1", revision.FfmpegProfile);
        Assert.Equal(2, revision.Assignments.Count);
        Assert.Matches("^[0-9a-f]{64}$", revision.Fingerprint);

        Assert.Equal(
            [NarrationCastRevisionStatus.Draft, NarrationCastRevisionStatus.Active, NarrationCastRevisionStatus.Historical],
            Enum.GetValues<NarrationCastRevisionStatus>());
    }

    [Fact]
    public void Fingerprint_v1_matches_the_frozen_length_prefixed_utf8_golden_vector()
    {
        var ownerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var seriesId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var revisionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var assignments = new[]
        {
            NarrationCastAssignment.Create(
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
                ownerId,
                seriesId,
                revisionId,
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                "Character Omega Ω",
                "provider-cast",
                "v1.2",
                "voice-omega-Ω",
                "+5%",
                "-2st",
                "+0dB"),
            NarrationCastAssignment.Create(
                Guid.Parse("55555555-5555-4555-8555-555555555555"),
                ownerId,
                seriesId,
                revisionId,
                Guid.Parse("01010101-0101-4101-8101-010101010101"),
                "Character Alpha",
                "provider-cast",
                "v1.2",
                "voice-alpha",
                "normal",
                "+1st",
                "-1dB")
        };

        var revision = NarrationCastRevision.Create(
            revisionId,
            ownerId,
            seriesId,
            42,
            "provider-main",
            "2026.08",
            "narrator-neutral",
            "1.05x",
            "+0st",
            "-1dB",
            275,
            1_250,
            "composition-v1",
            "aac-lc-48k-stereo",
            new DateTimeOffset(2026, 8, 11, 17, 45, 30, TimeSpan.FromHours(8)),
            assignments);

        Assert.Equal(
            "36c4426694a133f784e7ab2cde477ffa3a62ebaf2a56df7d9251f9bf8e263d4b",
            revision.Fingerprint);
    }

    [Fact]
    public void Reversed_assignment_input_order_produces_the_same_canonical_fingerprint()
    {
        var fixture = CreateFixture();
        var forward = fixture.Revision.Create();
        var reversed = (fixture.Revision with
        {
            Assignments = [fixture.Second.Create(), fixture.First.Create()]
        }).Create();

        Assert.Equal(forward.Fingerprint, reversed.Fingerprint);
        Assert.Equal(
            forward.Assignments.Select(assignment => assignment.CharacterId).OrderBy(id => id.ToString("N", null)),
            reversed.Assignments.Select(assignment => assignment.CharacterId).OrderBy(id => id.ToString("N", null)));
    }

    [Fact]
    public void Verify_fingerprint_is_order_independent_and_detects_materialized_assignment_tampering()
    {
        var revision = CreateFixture().Revision.Create();
        var assignmentsField = typeof(NarrationCastRevision).GetField(
            "_assignments",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var assignments = Assert.IsType<List<NarrationCastAssignment>>(assignmentsField!.GetValue(revision));

        assignments.Reverse();
        Assert.True(revision.VerifyFingerprint());

        assignments.RemoveAt(0);
        Assert.False(revision.VerifyFingerprint());
    }

    [Fact]
    public void Every_canonical_snapshot_field_class_changes_the_fingerprint()
    {
        var fixture = CreateFixture();
        var baseline = fixture.Revision.Create();
        var revision = fixture.Revision;
        var variants = new Dictionary<string, NarrationCastRevision>
        {
            ["narrator provider"] = (revision with { NarratorProvider = "provider-other" }).Create(),
            ["narrator provider version"] = (revision with { NarratorProviderVersion = "provider-main-v2" }).Create(),
            ["narrator voice"] = (revision with { NarratorVoice = "narrator-voice-other" }).Create(),
            ["narrator rate"] = (revision with { NarratorRate = "rate-other" }).Create(),
            ["narrator pitch"] = (revision with { NarratorPitch = "pitch-other" }).Create(),
            ["narrator volume"] = (revision with { NarratorVolume = "volume-other" }).Create(),
            ["default speaker pause"] = (revision with { DefaultSpeakerPauseMs = 351 }).Create(),
            ["chapter pause"] = (revision with { ChapterPauseMs = 901 }).Create(),
            ["composition version"] = (revision with { CompositionVersion = "composition-v2" }).Create(),
            ["ffmpeg profile"] = (revision with { FfmpegProfile = "audio-profile-v2" }).Create(),
            ["character set"] = (revision with { Assignments = [fixture.First.Create()] }).Create(),
            ["character id"] = WithFirstAssignment(fixture, fixture.First with { CharacterId = Guid.NewGuid() }),
            ["canonical name snapshot"] = WithFirstAssignment(
                fixture,
                fixture.First with { CanonicalNameSnapshot = "Character Alpha Prime" }),
            ["assignment provider"] = WithFirstAssignment(
                fixture,
                fixture.First with { VoiceProvider = "provider-cast-other" }),
            ["assignment provider version"] = WithFirstAssignment(
                fixture,
                fixture.First with { ProviderVersion = "provider-cast-v2" }),
            ["assignment voice"] = WithFirstAssignment(
                fixture,
                fixture.First with { Voice = "cast-voice-other" }),
            ["assignment rate"] = WithFirstAssignment(
                fixture,
                fixture.First with { Rate = "cast-rate-other" }),
            ["assignment pitch"] = WithFirstAssignment(
                fixture,
                fixture.First with { Pitch = "cast-pitch-other" }),
            ["assignment volume"] = WithFirstAssignment(
                fixture,
                fixture.First with { Volume = "cast-volume-other" })
        };

        Assert.All(
            variants,
            variant => Assert.False(
                string.Equals(baseline.Fingerprint, variant.Value.Fingerprint, StringComparison.Ordinal),
                $"Changing {variant.Key} must change the canonical fingerprint."));
    }

    [Fact]
    public void Revision_identity_number_status_epoch_timestamps_and_assignment_ids_are_not_fingerprinted()
    {
        var fixture = CreateFixture();
        var first = fixture.Revision.Create();
        var otherRevisionId = Guid.NewGuid();
        var second = (fixture.Revision with
        {
            Id = otherRevisionId,
            RevisionNumber = 99,
            CreatedAt = fixture.Revision.CreatedAt.AddDays(1),
            Assignments =
            [
                (fixture.First with { Id = Guid.NewGuid(), CastRevisionId = otherRevisionId }).Create(),
                (fixture.Second with { Id = Guid.NewGuid(), CastRevisionId = otherRevisionId }).Create()
            ]
        }).Create();

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        var fingerprint = first.Fingerprint;
        InvokeInternal(first, "Activate", 11, fixture.Revision.CreatedAt.AddMinutes(5));
        Assert.Equal(fingerprint, first.Fingerprint);
        InvokeInternal(first, "MakeHistorical");
        Assert.Equal(fingerprint, first.Fingerprint);
    }

    [Fact]
    public void Duplicate_character_and_cross_boundary_assignments_are_rejected()
    {
        var fixture = CreateFixture();
        var duplicateCharacter = fixture.Second with { CharacterId = fixture.First.CharacterId };
        var crossOwner = fixture.First with { OwnerId = Guid.NewGuid() };
        var crossSeries = fixture.First with { SeriesId = Guid.NewGuid() };
        var crossRevision = fixture.First with { CastRevisionId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => (fixture.Revision with
        {
            Assignments = [fixture.First.Create(), duplicateCharacter.Create()]
        }).Create());
        Assert.Throws<InvalidOperationException>(() => (fixture.Revision with
        {
            Assignments = [crossOwner.Create()]
        }).Create());
        Assert.Throws<InvalidOperationException>(() => (fixture.Revision with
        {
            Assignments = [crossSeries.Create()]
        }).Create());
        Assert.Throws<InvalidOperationException>(() => (fixture.Revision with
        {
            Assignments = [crossRevision.Create()]
        }).Create());
    }

    [Fact]
    public void Duplicate_non_empty_assignment_ids_are_rejected_even_for_distinct_characters()
    {
        var fixture = CreateFixture();
        var duplicateId = fixture.Second with { Id = fixture.First.Id };
        Assert.NotEqual(fixture.First.CharacterId, duplicateId.CharacterId);

        Assert.Throws<InvalidOperationException>(() => (fixture.Revision with
        {
            Assignments = [fixture.First.Create(), duplicateId.Create()]
        }).Create());
    }

    [Fact]
    public void Empty_revision_and_assignment_ids_are_rejected()
    {
        var fixture = CreateFixture();

        Assert.Throws<ArgumentException>(() => (fixture.Revision with { Id = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.Revision with { OwnerId = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.Revision with { SeriesId = Guid.Empty }).Create());

        Assert.Throws<ArgumentException>(() => (fixture.First with { Id = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.First with { OwnerId = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.First with { SeriesId = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.First with { CastRevisionId = Guid.Empty }).Create());
        Assert.Throws<ArgumentException>(() => (fixture.First with { CharacterId = Guid.Empty }).Create());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0value")]
    [InlineData("bad\u0001value")]
    [InlineData("bad\u200Bvalue")]
    [InlineData("\u200B\u200C\u2060")]
    public void Empty_control_format_and_invisible_text_is_rejected_without_an_observable_revision(string invalid)
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision;
        var invalidRevisions = new RevisionInput[]
        {
            revision with { NarratorProvider = invalid },
            revision with { NarratorProviderVersion = invalid },
            revision with { NarratorVoice = invalid },
            revision with { NarratorRate = invalid },
            revision with { NarratorPitch = invalid },
            revision with { NarratorVolume = invalid },
            revision with { CompositionVersion = invalid },
            revision with { FfmpegProfile = invalid }
        };
        var assignment = fixture.First;
        var invalidAssignments = new AssignmentInput[]
        {
            assignment with { CanonicalNameSnapshot = invalid },
            assignment with { VoiceProvider = invalid },
            assignment with { ProviderVersion = invalid },
            assignment with { Voice = invalid },
            assignment with { Rate = invalid },
            assignment with { Pitch = invalid },
            assignment with { Volume = invalid }
        };

        Assert.All(invalidRevisions, input => Assert.Throws<ArgumentException>(input.Create));
        Assert.All(invalidAssignments, input => Assert.Throws<ArgumentException>(input.Create));
    }

    [Fact]
    public void Overlength_text_and_out_of_range_revision_and_pauses_are_rejected()
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision;
        var invalidRevisions = new RevisionInput[]
        {
            revision with { RevisionNumber = 0 },
            revision with { NarratorProvider = new string('p', MaximumProviderLength + 1) },
            revision with { NarratorProviderVersion = new string('v', MaximumProviderLength + 1) },
            revision with { NarratorVoice = new string('v', MaximumVoiceLength + 1) },
            revision with { NarratorRate = new string('r', MaximumParameterLength + 1) },
            revision with { NarratorPitch = new string('p', MaximumParameterLength + 1) },
            revision with { NarratorVolume = new string('v', MaximumParameterLength + 1) },
            revision with { CompositionVersion = new string('c', MaximumProviderLength + 1) },
            revision with { FfmpegProfile = new string('f', MaximumVoiceLength + 1) },
            revision with { DefaultSpeakerPauseMs = -1 },
            revision with { DefaultSpeakerPauseMs = 60_001 },
            revision with { ChapterPauseMs = -1 },
            revision with { ChapterPauseMs = 60_001 }
        };
        var assignment = fixture.First;
        var invalidAssignments = new AssignmentInput[]
        {
            assignment with { CanonicalNameSnapshot = new string('n', MaximumCharacterNameLength + 1) },
            assignment with { VoiceProvider = new string('p', MaximumProviderLength + 1) },
            assignment with { ProviderVersion = new string('v', MaximumProviderLength + 1) },
            assignment with { Voice = new string('v', MaximumVoiceLength + 1) },
            assignment with { Rate = new string('r', MaximumParameterLength + 1) },
            assignment with { Pitch = new string('p', MaximumParameterLength + 1) },
            assignment with { Volume = new string('v', MaximumParameterLength + 1) }
        };

        Assert.All(invalidRevisions, input => Assert.ThrowsAny<ArgumentException>(input.Create));
        Assert.All(invalidAssignments, input => Assert.Throws<ArgumentException>(input.Create));
    }

    [Fact]
    public void Text_fields_at_their_exact_storage_maximum_are_accepted_and_preserved()
    {
        var fixture = CreateFixture();
        var narratorProvider = new string('A', MaximumProviderLength);
        var narratorProviderVersion = new string('B', MaximumProviderLength);
        var narratorVoice = new string('C', MaximumVoiceLength);
        var narratorRate = new string('D', MaximumParameterLength);
        var narratorPitch = new string('E', MaximumParameterLength);
        var narratorVolume = new string('F', MaximumParameterLength);
        var compositionVersion = new string('G', MaximumProviderLength);
        var ffmpegProfile = new string('H', MaximumVoiceLength);
        var canonicalNameSnapshot = new string('I', MaximumCharacterNameLength);
        var voiceProvider = new string('J', MaximumProviderLength);
        var providerVersion = new string('K', MaximumProviderLength);
        var voice = new string('L', MaximumVoiceLength);
        var rate = new string('M', MaximumParameterLength);
        var pitch = new string('N', MaximumParameterLength);
        var volume = new string('O', MaximumParameterLength);
        var assignment = (fixture.First with
        {
            CanonicalNameSnapshot = canonicalNameSnapshot,
            VoiceProvider = voiceProvider,
            ProviderVersion = providerVersion,
            Voice = voice,
            Rate = rate,
            Pitch = pitch,
            Volume = volume
        }).Create();
        var revision = (fixture.Revision with
        {
            NarratorProvider = narratorProvider,
            NarratorProviderVersion = narratorProviderVersion,
            NarratorVoice = narratorVoice,
            NarratorRate = narratorRate,
            NarratorPitch = narratorPitch,
            NarratorVolume = narratorVolume,
            CompositionVersion = compositionVersion,
            FfmpegProfile = ffmpegProfile,
            Assignments = [assignment]
        }).Create();

        Assert.Equal(narratorProvider, revision.NarratorProvider);
        Assert.Equal(narratorProviderVersion, revision.NarratorProviderVersion);
        Assert.Equal(narratorVoice, revision.NarratorVoice);
        Assert.Equal(narratorRate, revision.NarratorRate);
        Assert.Equal(narratorPitch, revision.NarratorPitch);
        Assert.Equal(narratorVolume, revision.NarratorVolume);
        Assert.Equal(compositionVersion, revision.CompositionVersion);
        Assert.Equal(ffmpegProfile, revision.FfmpegProfile);

        var preservedAssignment = Assert.Single(revision.Assignments);
        Assert.Equal(canonicalNameSnapshot, preservedAssignment.CanonicalNameSnapshot);
        Assert.Equal(voiceProvider, preservedAssignment.VoiceProvider);
        Assert.Equal(providerVersion, preservedAssignment.ProviderVersion);
        Assert.Equal(voice, preservedAssignment.Voice);
        Assert.Equal(rate, preservedAssignment.Rate);
        Assert.Equal(pitch, preservedAssignment.Pitch);
        Assert.Equal(volume, preservedAssignment.Volume);
    }

    [Fact]
    public void Pause_fields_accept_and_preserve_both_inclusive_boundaries()
    {
        var fixture = CreateFixture();
        var lowerBoundary = (fixture.Revision with
        {
            DefaultSpeakerPauseMs = 0,
            ChapterPauseMs = 0
        }).Create();
        var upperBoundary = (fixture.Revision with
        {
            DefaultSpeakerPauseMs = 60_000,
            ChapterPauseMs = 60_000
        }).Create();

        Assert.Equal(0, lowerBoundary.DefaultSpeakerPauseMs);
        Assert.Equal(0, lowerBoundary.ChapterPauseMs);
        Assert.Equal(60_000, upperBoundary.DefaultSpeakerPauseMs);
        Assert.Equal(60_000, upperBoundary.ChapterPauseMs);
    }

    [Fact]
    public void Provider_neutral_printable_voice_parameters_are_accepted()
    {
        var fixture = CreateFixture();
        var assignment = (fixture.First with
        {
            VoiceProvider = "generic-provider",
            ProviderVersion = "release/2026.08",
            Voice = "voice descriptor A",
            Rate = "relative speed medium",
            Pitch = "register neutral",
            Volume = "gain standard"
        }).Create();

        Assert.Equal("relative speed medium", assignment.Rate);
        Assert.Equal("register neutral", assignment.Pitch);
        Assert.Equal("gain standard", assignment.Volume);
    }

    [Fact]
    public void Assignments_and_fingerprint_are_immutable_and_the_collection_is_readonly_and_defensively_copied()
    {
        var fixture = CreateFixture();
        var source = new List<NarrationCastAssignment>
        {
            fixture.First.Create(),
            fixture.Second.Create()
        };
        var revision = (fixture.Revision with { Assignments = source }).Create();
        var fingerprint = revision.Fingerprint;

        source.Clear();

        Assert.Equal(2, revision.Assignments.Count);
        Assert.Equal(fingerprint, revision.Fingerprint);
        var assignmentsField = typeof(NarrationCastRevision).GetField(
            "_assignments",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(assignmentsField);
        Assert.Equal(typeof(List<NarrationCastAssignment>), assignmentsField.FieldType);
        Assert.True(assignmentsField.IsInitOnly);
        Assert.NotSame(source, revision.Assignments);
        var mutableView = Assert.IsAssignableFrom<ICollection<NarrationCastAssignment>>(revision.Assignments);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Add(fixture.First.Create()));
        Assert.Throws<NotSupportedException>(() => mutableView.Remove(revision.Assignments[0]));
        Assert.Throws<NotSupportedException>(mutableView.Clear);
        Assert.Equal(2, revision.Assignments.Count);
        Assert.DoesNotContain(
            typeof(NarrationCastRevision).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            method =>
                (method.IsPublic || method.IsAssembly) &&
                method.Name != "get_Assignments" &&
                method.Name.Contains("Assignment", StringComparison.Ordinal));
        Assert.Null(typeof(NarrationCastRevision).GetProperty(nameof(NarrationCastRevision.Fingerprint))!
            .GetSetMethod(nonPublic: true));

        var immutableRevisionProperties = new[]
        {
            nameof(NarrationCastRevision.Id),
            nameof(NarrationCastRevision.OwnerId),
            nameof(NarrationCastRevision.SeriesId),
            nameof(NarrationCastRevision.RevisionNumber),
            nameof(NarrationCastRevision.NarratorProvider),
            nameof(NarrationCastRevision.NarratorProviderVersion),
            nameof(NarrationCastRevision.NarratorVoice),
            nameof(NarrationCastRevision.NarratorRate),
            nameof(NarrationCastRevision.NarratorPitch),
            nameof(NarrationCastRevision.NarratorVolume),
            nameof(NarrationCastRevision.DefaultSpeakerPauseMs),
            nameof(NarrationCastRevision.ChapterPauseMs),
            nameof(NarrationCastRevision.CompositionVersion),
            nameof(NarrationCastRevision.FfmpegProfile),
            nameof(NarrationCastRevision.CreatedAt)
        };
        Assert.All(
            immutableRevisionProperties,
            propertyName => Assert.Null(typeof(NarrationCastRevision).GetProperty(propertyName)!
                .GetSetMethod(nonPublic: true)));
        Assert.All(
            typeof(NarrationCastAssignment).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => Assert.Null(property.GetSetMethod(nonPublic: true)));
    }

    [Fact]
    public void Draft_can_activate_with_a_positive_epoch_then_become_historical_and_only_active_is_job_eligible()
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision.Create();
        var activatedAt = fixture.Revision.CreatedAt.AddMinutes(5);

        Assert.Equal(NarrationCastRevisionStatus.Draft, revision.Status);
        Assert.Null(revision.EpochNumber);
        Assert.Null(revision.ActivatedAt);
        Assert.False(revision.CanBeUsedByRegularJob);

        InvokeInternal(revision, "Activate", 12, activatedAt);

        Assert.Equal(NarrationCastRevisionStatus.Active, revision.Status);
        Assert.Equal(12, revision.EpochNumber);
        Assert.Equal(activatedAt, revision.ActivatedAt);
        Assert.True(revision.CanBeUsedByRegularJob);

        InvokeInternal(revision, "MakeHistorical");

        Assert.Equal(NarrationCastRevisionStatus.Historical, revision.Status);
        Assert.Equal(12, revision.EpochNumber);
        Assert.Equal(activatedAt, revision.ActivatedAt);
        Assert.False(revision.CanBeUsedByRegularJob);
    }

    [Fact]
    public void Activation_at_the_exact_creation_time_is_accepted()
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision.Create();

        InvokeInternal(revision, "Activate", 12, fixture.Revision.CreatedAt);

        Assert.Equal(NarrationCastRevisionStatus.Active, revision.Status);
        Assert.Equal(12, revision.EpochNumber);
        Assert.Equal(fixture.Revision.CreatedAt, revision.ActivatedAt);
    }

    [Fact]
    public void Activation_before_creation_time_is_rejected_atomically()
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision.Create();
        var before = (revision.Status, revision.EpochNumber, revision.ActivatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InvokeInternal(revision, "Activate", 12, fixture.Revision.CreatedAt.AddTicks(-1)));

        Assert.Equal(before, (revision.Status, revision.EpochNumber, revision.ActivatedAt));
    }

    [Fact]
    public void Illegal_transitions_and_invalid_epoch_fail_without_mutating_status_epoch_or_activation_time()
    {
        var fixture = CreateFixture();
        var revision = fixture.Revision.Create();
        var activatedAt = fixture.Revision.CreatedAt.AddMinutes(5);

        AssertIllegalTransitionIsAtomic(revision, "Activate", 0, activatedAt);
        AssertIllegalTransitionIsAtomic(revision, "MakeHistorical");

        InvokeInternal(revision, "Activate", 12, activatedAt);
        AssertIllegalTransitionIsAtomic(revision, "Activate", 13, activatedAt.AddMinutes(1));

        InvokeInternal(revision, "MakeHistorical");
        AssertIllegalTransitionIsAtomic(revision, "Activate", 14, activatedAt.AddMinutes(2));
        AssertIllegalTransitionIsAtomic(revision, "MakeHistorical");
    }

    private static NarrationCastRevision WithFirstAssignment(SnapshotFixture fixture, AssignmentInput first) =>
        (fixture.Revision with { Assignments = [first.Create(), fixture.Second.Create()] }).Create();

    private static void AssertIllegalTransitionIsAtomic(
        NarrationCastRevision revision,
        string methodName,
        params object[] arguments)
    {
        var before = (revision.Status, revision.EpochNumber, revision.ActivatedAt);

        Assert.ThrowsAny<Exception>(() => InvokeInternal(revision, methodName, arguments));

        Assert.Equal(before, (revision.Status, revision.EpochNumber, revision.ActivatedAt));
    }

    private static void InvokeInternal(
        NarrationCastRevision revision,
        string methodName,
        params object[] arguments)
    {
        var method = typeof(NarrationCastRevision).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method.IsAssembly, $"{methodName} must remain narrowly internal.");
        try
        {
            method.Invoke(revision, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static SnapshotFixture CreateFixture()
    {
        var ownerId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var first = new AssignmentInput(
            Guid.NewGuid(),
            ownerId,
            seriesId,
            revisionId,
            Guid.NewGuid(),
            "Character Alpha",
            "provider-cast",
            "provider-cast-v1",
            "cast-voice-alpha",
            "cast-rate-default",
            "cast-pitch-default",
            "cast-volume-default");
        var second = first with
        {
            Id = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            CanonicalNameSnapshot = "Character Beta",
            Voice = "cast-voice-beta"
        };
        var revision = new RevisionInput(
            revisionId,
            ownerId,
            seriesId,
            7,
            "provider-main",
            "provider-main-v1",
            "narrator-voice",
            "rate-default",
            "pitch-default",
            "volume-default",
            350,
            900,
            "composition-v1",
            "audio-profile-v1",
            new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.Zero),
            [first.Create(), second.Create()]);
        return new SnapshotFixture(revision, first, second);
    }

    private sealed record SnapshotFixture(
        RevisionInput Revision,
        AssignmentInput First,
        AssignmentInput Second);

    private sealed record RevisionInput(
        Guid Id,
        Guid OwnerId,
        Guid SeriesId,
        int RevisionNumber,
        string NarratorProvider,
        string NarratorProviderVersion,
        string NarratorVoice,
        string NarratorRate,
        string NarratorPitch,
        string NarratorVolume,
        int DefaultSpeakerPauseMs,
        int ChapterPauseMs,
        string CompositionVersion,
        string FfmpegProfile,
        DateTimeOffset CreatedAt,
        IReadOnlyCollection<NarrationCastAssignment> Assignments)
    {
        internal NarrationCastRevision Create() => NarrationCastRevision.Create(
            Id,
            OwnerId,
            SeriesId,
            RevisionNumber,
            NarratorProvider,
            NarratorProviderVersion,
            NarratorVoice,
            NarratorRate,
            NarratorPitch,
            NarratorVolume,
            DefaultSpeakerPauseMs,
            ChapterPauseMs,
            CompositionVersion,
            FfmpegProfile,
            CreatedAt,
            Assignments);
    }

    private sealed record AssignmentInput(
        Guid Id,
        Guid OwnerId,
        Guid SeriesId,
        Guid CastRevisionId,
        Guid CharacterId,
        string CanonicalNameSnapshot,
        string VoiceProvider,
        string ProviderVersion,
        string Voice,
        string Rate,
        string Pitch,
        string Volume)
    {
        internal NarrationCastAssignment Create() => NarrationCastAssignment.Create(
            Id,
            OwnerId,
            SeriesId,
            CastRevisionId,
            CharacterId,
            CanonicalNameSnapshot,
            VoiceProvider,
            ProviderVersion,
            Voice,
            Rate,
            Pitch,
            Volume);
    }
}
