using System.Buffers.Binary;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Characters;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class CharacterVoiceProfileReplacementPostgreSqlTests
{
    private const string PreviousMigration = "20260816154216_EnforceLocalLlmAnalysisOwnerScope";

    [Fact]
    public async Task Replace_executes_delete_before_insert_and_rolls_back_a_second_save_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);

        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "storyvoice-character-voice-postgres-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var ownerId = Guid.NewGuid();
            var successfulCharacterId = Guid.NewGuid();
            var successfulDesignId = Guid.NewGuid();
            var rollbackCharacterId = Guid.NewGuid();
            var rollbackDesignId = Guid.NewGuid();
            var baseOptions = new DbContextOptionsBuilder<StoryVoiceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .Options;

            await using (var setup = new StoryVoiceDbContext(baseOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                setup.Users.Add(CreateUser(ownerId));
                setup.CharacterProfiles.AddRange(
                    CreateCharacter(successfulCharacterId, ownerId, "成功取代角色"),
                    CreateCharacter(rollbackCharacterId, ownerId, "回復驗證角色"));
                setup.CharacterVoiceProfiles.AddRange(
                    CreateDesign(successfulDesignId, ownerId, successfulCharacterId),
                    CreateDesign(rollbackDesignId, ownerId, rollbackCharacterId));
                await setup.SaveChangesAsync(cancellationToken);
            }

            var commandOrder = new CharacterVoiceCommandInterceptor();
            var successfulOptions = new DbContextOptionsBuilder<StoryVoiceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .AddInterceptors(commandOrder)
                .Options;
            var successfulProvider = new FakeThreeWaVoiceProfileClient("pg-ok-task");
            await using (var successfulDb = new StoryVoiceDbContext(successfulOptions))
            {
                var service = CreateService(successfulDb, ownerId, storageRoot, successfulProvider);
                const string transcript = "這是成功取代測試的逐字稿。";
                var audioBytes = CreateMinimalWav();
                await using var referenceAudio = new MemoryStream(audioBytes);
                await using var consentReceipt = new MemoryStream(
                    CreateConsentReceipt(audioBytes, transcript));

                var replacement = await service.ReplaceDesignedWithCloneAsync(
                    successfulCharacterId,
                    successfulDesignId,
                    transcript,
                    referenceAudio,
                    "success.wav",
                    consentReceipt,
                    "success-consent.json",
                    rightsAttested: true,
                    cancellationToken);

                Assert.NotNull(replacement);
                Assert.Equal("Clone", replacement.Mode);
            }

            var deleteIndex = commandOrder.Commands.FindIndex(IsCharacterVoiceDelete);
            var insertIndex = commandOrder.Commands.FindIndex(IsCharacterVoiceInsert);
            Assert.True(deleteIndex >= 0, "Expected a character_voice_profiles DELETE command.");
            Assert.True(insertIndex > deleteIndex, "The replacement INSERT must execute after the slot DELETE.");

            await using (var verification = new StoryVoiceDbContext(baseOptions))
            {
                var successfulProfile = await verification.CharacterVoiceProfiles.SingleAsync(
                    profile => profile.OwnerId == ownerId
                        && profile.CharacterProfileId == successfulCharacterId,
                    cancellationToken);
                Assert.Equal(CharacterVoiceProfileMode.Clone, successfulProfile.Mode);
                Assert.NotEqual(successfulDesignId, successfulProfile.Id);
                var operation = await verification.CharacterVoiceProfileOperations.SingleAsync(
                    candidate => candidate.CharacterProfileId == successfulCharacterId,
                    cancellationToken);
                Assert.Equal(CharacterVoiceProfileOperationState.Activated, operation.State);
                Assert.Equal(successfulProfile.Id, operation.NewProfileId);
                Assert.Equal("pg-ok-task", operation.RemoteTaskId);
            }

            var secondSaveFailure = new CharacterVoiceCommandInterceptor(failCloneInsert: true);
            var failingOptions = new DbContextOptionsBuilder<StoryVoiceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .AddInterceptors(secondSaveFailure)
                .Options;
            var failingProvider = new FakeThreeWaVoiceProfileClient("pg-fail-task");
            await using (var failingDb = new StoryVoiceDbContext(failingOptions))
            {
                var service = CreateService(failingDb, ownerId, storageRoot, failingProvider);
                const string transcript = "這是交易回復測試的逐字稿。";
                var audioBytes = CreateMinimalWav();
                await using var referenceAudio = new MemoryStream(audioBytes);
                await using var consentReceipt = new MemoryStream(
                    CreateConsentReceipt(audioBytes, transcript));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    service.ReplaceDesignedWithCloneAsync(
                        rollbackCharacterId,
                        rollbackDesignId,
                        transcript,
                        referenceAudio,
                        "rollback.wav",
                        consentReceipt,
                        "rollback-consent.json",
                        rightsAttested: true,
                        cancellationToken));
                Assert.IsType<InvalidOperationException>(exception.InnerException);
            }

            await using (var verification = new StoryVoiceDbContext(baseOptions))
            {
                var rolledBackProfile = await verification.CharacterVoiceProfiles.SingleAsync(
                    profile => profile.OwnerId == ownerId
                        && profile.CharacterProfileId == rollbackCharacterId,
                    cancellationToken);
                Assert.Equal(rollbackDesignId, rolledBackProfile.Id);
                Assert.Equal(CharacterVoiceProfileMode.Design, rolledBackProfile.Mode);
                var operation = await verification.CharacterVoiceProfileOperations.SingleAsync(
                    candidate => candidate.CharacterProfileId == rollbackCharacterId,
                    cancellationToken);
                Assert.Equal(CharacterVoiceProfileOperationState.NeedsAttention, operation.State);
                Assert.Equal("local_activation_uncertain", operation.SafeErrorCode);
                Assert.Equal("pg-fail-task", operation.RemoteTaskId);
            }
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Migration_preserves_legacy_40_character_task_ids_and_backfills_ASR_drafts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var ownerId = Guid.NewGuid();
        var failedCharacterId = Guid.NewGuid();
        var awaitingCharacterId = Guid.NewGuid();
        var failedProfileId = Guid.NewGuid();
        var awaitingProfileId = Guid.NewGuid();
        var legacyTaskId = $"route_{new string('a', 34)}";
        var now = DateTimeOffset.UtcNow;

        await using (var previous = new StoryVoiceDbContext(options))
        {
            await previous.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
            previous.Users.Add(CreateUser(ownerId));
            previous.CharacterProfiles.AddRange(
                CreateCharacter(failedCharacterId, ownerId, "舊失敗 Clone"),
                CreateCharacter(awaitingCharacterId, ownerId, "舊 ASR 草稿"));
            await previous.SaveChangesAsync(cancellationToken);

            await InsertLegacyCloneAsync(
                previous,
                failedProfileId,
                ownerId,
                failedCharacterId,
                legacyTaskId,
                transcript: null,
                status: "Failed",
                now,
                cancellationToken);
            await InsertLegacyCloneAsync(
                previous,
                awaitingProfileId,
                ownerId,
                awaitingCharacterId,
                "legacy-task",
                transcript: "舊版供應商辨識草稿。",
                status: "AwaitingTranscriptConfirmation",
                now,
                cancellationToken);

            await previous.GetService<IMigrator>().MigrateAsync(cancellationToken: cancellationToken);
        }

        await using var verification = new StoryVoiceDbContext(options);
        var failed = await verification.CharacterVoiceProfiles.SingleAsync(
            profile => profile.Id == failedProfileId,
            cancellationToken);
        Assert.Equal(40, legacyTaskId.Length);
        Assert.Equal(legacyTaskId, failed.VoiceProfileTaskId);
        Assert.Equal(CharacterVoiceProfileStatus.Failed, failed.Status);
        Assert.Null(failed.ExpectedTranscript);
        Assert.Null(failed.AsrDraftTranscript);

        var awaiting = await verification.CharacterVoiceProfiles.SingleAsync(
            profile => profile.Id == awaitingProfileId,
            cancellationToken);
        Assert.Equal("舊版供應商辨識草稿。", awaiting.Transcript);
        Assert.Equal("舊版供應商辨識草稿。", awaiting.AsrDraftTranscript);
        Assert.Null(awaiting.ExpectedTranscript);
        Assert.False(await verification.CharacterVoiceProfileOperations.AnyAsync(cancellationToken));
        var operationEntity = verification.Model.FindEntityType(typeof(CharacterVoiceProfileOperation))!;
        Assert.Equal(
            CharacterVoiceConsentEvidence.MaximumRecorderNameLength,
            operationEntity.FindProperty(nameof(CharacterVoiceProfileOperation.RecorderName))!.GetMaxLength());
        Assert.Equal(
            CharacterVoiceConsentEvidence.MaximumVersionLength,
            operationEntity.FindProperty(nameof(CharacterVoiceProfileOperation.EvidenceVersion))!.GetMaxLength());
        Assert.Equal(
            CharacterVoiceProfileOperation.MaximumStoredRemoteTaskIdLength,
            verification.Model.FindEntityType(typeof(CharacterVoiceProfile))!
                .FindProperty(nameof(CharacterVoiceProfile.VoiceProfileTaskId))!
                .GetMaxLength());
    }

    [Fact]
    public async Task Operation_evidence_constraints_reject_invalid_rows_and_columns_have_no_defaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var ownerId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string transcript = "資料庫證據約束測試。";

        await using var db = new StoryVoiceDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.Add(CreateUser(ownerId));
        db.CharacterProfiles.Add(CreateCharacter(characterId, ownerId, "證據約束角色"));
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var evidence = CharacterVoiceConsentEvidence.Create(
            "postgres-test-recorder",
            today.AddDays(-1),
            today,
            CharacterVoiceConsentTypes.SelfRecorded,
            [CharacterVoiceConsentScopes.PrivateEvaluation],
            new string('b', 64),
            new string('c', 64),
            CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(transcript),
            CharacterVoiceConsentEvidence.CurrentEvidenceVersion,
            CharacterVoiceConsentEvidence.CurrentAttestationVersion,
            today);
        var operation = CharacterVoiceProfileOperation.StageCreate(
            Guid.NewGuid(),
            ownerId,
            characterId,
            Guid.NewGuid(),
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            evidence,
            transcript,
            "evidence/reference.wav",
            new string('a', 64),
            10,
            ownerId,
            "postgres-test-key",
            now);
        db.CharacterVoiceProfileOperations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using (var defaultsCommand = db.Database.GetDbConnection().CreateCommand())
        {
            defaultsCommand.CommandText =
                """
                SELECT count(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'character_voice_profile_operations'
                  AND column_name IN (
                      'AttestationVersion', 'CommercialUseAllowed', 'ConsentReceiptSha256',
                      'ConsentRecordSha256', 'ConsentSignedDate', 'EvidenceVersion',
                      'ExpectedTranscriptSha256', 'FormalNarrationAllowed',
                      'PrivateEvaluationAllowed', 'PublicDistributionAllowed',
                      'RecorderName', 'RecordingDate')
                  AND column_default IS NOT NULL;
                """;
            Assert.Equal(0L, Convert.ToInt64(await defaultsCommand.ExecuteScalarAsync(cancellationToken)));
        }

        await AssertCheckViolationAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE character_voice_profile_operations SET \"PrivateEvaluationAllowed\" = FALSE WHERE \"Id\" = {operation.Id}",
                cancellationToken));
        await AssertCheckViolationAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE character_voice_profile_operations SET \"ConsentReceiptSha256\" = {new string('z', 64)} WHERE \"Id\" = {operation.Id}",
                cancellationToken));
        await AssertCheckViolationAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE character_voice_profile_operations SET \"ConsentSignedDate\" = {today.AddDays(-2)} WHERE \"Id\" = {operation.Id}",
                cancellationToken));
        await AssertCheckViolationAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE character_voice_profile_operations SET \"RightsConfirmedByUserId\" = {Guid.NewGuid()} WHERE \"Id\" = {operation.Id}",
                cancellationToken));
        await AssertCheckViolationAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE character_voice_profile_operations
                SET "State" = 'Activated', "RemoteTaskId" = 'task-1', "RemotePreparedAt" = {now}
                WHERE "Id" = {operation.Id}
                """,
                cancellationToken));
    }

    [Fact]
    public async Task Concurrent_character_delete_wins_row_lock_and_stage_never_calls_provider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "storyvoice-character-voice-postgres-race-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var ownerId = Guid.NewGuid();
            var characterId = Guid.NewGuid();
            var baseOptions = new DbContextOptionsBuilder<StoryVoiceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .Options;
            await using (var setup = new StoryVoiceDbContext(baseOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                setup.Users.Add(CreateUser(ownerId));
                setup.CharacterProfiles.Add(CreateCharacter(characterId, ownerId, "刪除先勝角色"));
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var deleteWinner = new StoryVoiceDbContext(baseOptions);
            await using var deleteTransaction = await deleteWinner.Database.BeginTransactionAsync(cancellationToken);
            _ = await deleteWinner.CharacterProfiles
                .FromSqlInterpolated(
                    $"""
                    SELECT * FROM character_profiles
                    WHERE "OwnerId" = {ownerId} AND "Id" = {characterId}
                    FOR UPDATE
                    """)
                .SingleAsync(cancellationToken);

            var forUpdateStarted = new ForUpdateStartedInterceptor();
            var stageOptions = new DbContextOptionsBuilder<StoryVoiceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .AddInterceptors(forUpdateStarted)
                .Options;
            var provider = new FakeThreeWaVoiceProfileClient("must-not-be-called");
            await using var stageDb = new StoryVoiceDbContext(stageOptions);
            var service = CreateService(stageDb, ownerId, storageRoot, provider);
            const string transcript = "刪除先勝時不可送出遠端。";
            var audioBytes = CreateMinimalWav();
            await using var referenceAudio = new MemoryStream(audioBytes);
            await using var consentReceipt = new MemoryStream(CreateConsentReceipt(audioBytes, transcript));
            var stageTask = service.CreateClonedAsync(
                characterId,
                "Base",
                sceneCode: null,
                transcript,
                referenceAudio,
                "delete-wins.wav",
                consentReceipt,
                "delete-wins-consent.json",
                rightsAttested: true,
                cancellationToken);

            await forUpdateStarted.Started.Task.WaitAsync(cancellationToken);
            await deleteWinner.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM character_profiles WHERE \"OwnerId\" = {ownerId} AND \"Id\" = {characterId}",
                cancellationToken);
            await deleteTransaction.CommitAsync(cancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => stageTask);
            Assert.Contains("未送出至 3wa", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, provider.PrepareCount);
            await using var verification = new StoryVoiceDbContext(baseOptions);
            Assert.False(await verification.CharacterProfiles.AnyAsync(
                character => character.Id == characterId,
                cancellationToken));
            Assert.False(await verification.CharacterVoiceProfileOperations.AnyAsync(
                candidate => candidate.CharacterProfileId == characterId,
                cancellationToken));
            Assert.False(Directory.Exists(storageRoot)
                && Directory.EnumerateFiles(storageRoot, "*.wav", SearchOption.AllDirectories).Any());
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static CharacterVoiceProfileService CreateService(
        StoryVoiceDbContext dbContext,
        Guid ownerId,
        string storageRoot,
        IThreeWaVoiceProfileClient provider) =>
        new(
            dbContext,
            new FixedCurrentUser(ownerId),
            new LocalCharacterVoiceAudioStorage(Options.Create(new CharacterVoiceStorageOptions
            {
                RootPath = storageRoot,
            })),
            provider,
            Options.Create(new ThreeWaAiHubOptions
            {
                ApiToken = "postgres-test-token-not-a-production-secret",
                CredentialKeyId = "postgres-test-key",
            }));

    private static ApplicationUser CreateUser(Guid ownerId) =>
        new()
        {
            Id = ownerId,
            UserName = "voice-replacement-postgres",
            NormalizedUserName = "VOICE-REPLACEMENT-POSTGRES",
            Email = "voice-replacement-postgres@example.invalid",
            NormalizedEmail = "VOICE-REPLACEMENT-POSTGRES@EXAMPLE.INVALID",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };

    private static CharacterProfile CreateCharacter(Guid id, Guid ownerId, string name) =>
        CharacterProfile.Create(
            id,
            ownerId,
            name,
            avatarRelativePath: null,
            age: null,
            gender: null,
            birthday: null,
            personality: null,
            catchphrase: null,
            background: null,
            speakingStyle: null,
            DateTimeOffset.UtcNow);

    private static CharacterVoiceProfile CreateDesign(Guid id, Guid ownerId, Guid characterId) =>
        CharacterVoiceProfile.CreateDesign(
            id,
            ownerId,
            characterId,
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            "原本的文字設計聲線",
            DateTimeOffset.UtcNow);

    private static Task<int> InsertLegacyCloneAsync(
        StoryVoiceDbContext dbContext,
        Guid profileId,
        Guid ownerId,
        Guid characterProfileId,
        string taskId,
        string? transcript,
        string status,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO character_voice_profiles
                ("Id", "OwnerId", "CharacterProfileId", "Kind", "SceneCode", "Mode",
                 "ConsentType", "ReferenceAudioRelativePath", "ReferenceAudioSha256",
                 "ReferenceAudioDurationSeconds", "VoicePromptText", "Transcript",
                 "TranscriptConfirmedAt", "VoiceProfileTaskId", "Status", "RightsConfirmedAt",
                 "RightsConfirmedByUserId", "CreatedAt", "UpdatedAt", "ConcurrencyStamp")
            VALUES
                ({profileId}, {ownerId}, {characterProfileId}, 'Base', NULL, 'Clone',
                 'self_recorded', 'legacy/reference.wav', {new string('a', 64)},
                 10, NULL, {transcript}, NULL, {taskId}, {status}, {now}, {ownerId},
                 {now}, {now}, {Guid.NewGuid()});
            """,
            cancellationToken);

    private static bool IsCharacterVoiceDelete(string commandText) =>
        commandText.Contains("DELETE FROM character_voice_profiles", StringComparison.OrdinalIgnoreCase)
        || commandText.Contains("DELETE FROM \"character_voice_profiles\"", StringComparison.OrdinalIgnoreCase);

    private static async Task AssertCheckViolationAsync(Func<Task<int>> action)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private static bool IsCharacterVoiceInsert(string commandText) =>
        commandText.Contains("INSERT INTO character_voice_profiles", StringComparison.OrdinalIgnoreCase)
        || commandText.Contains("INSERT INTO \"character_voice_profiles\"", StringComparison.OrdinalIgnoreCase);

    private static byte[] CreateMinimalWav()
    {
        const int sampleRate = 48_000;
        const ushort channels = 1;
        const ushort bitsPerSample = 16;
        const ushort blockAlign = channels * (bitsPerSample / 8);
        const int durationSeconds = 10;
        const int byteRate = sampleRate * blockAlign;
        const int dataLength = byteRate * durationSeconds;
        var wav = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(wav.Length - 8));
        "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
        "fmt "u8.CopyTo(wav.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), dataLength);
        return wav;
    }

    private static byte[] CreateConsentReceipt(byte[] audioBytes, string transcript)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = CharacterVoiceConsentEvidence.CurrentEvidenceVersion,
            recorderName = "postgres-test-recorder",
            recordingDate = date,
            consentSignedDate = date,
            consentType = CharacterVoiceConsentTypes.SelfRecorded,
            usageScopes = new[]
            {
                CharacterVoiceConsentScopes.PrivateEvaluation,
                CharacterVoiceConsentScopes.FormalNarration,
            },
            recordingSha256 = Convert.ToHexString(SHA256.HashData(audioBytes)).ToLowerInvariant(),
            expectedTranscriptCanonicalSha256 =
                CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(transcript),
            consentSha256 = new string('b', 64),
            subjectAttestationVersion = CharacterVoiceConsentEvidence.CurrentAttestationVersion,
            generatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
        });
    }

    private sealed record FixedCurrentUser(Guid UserId) : ICurrentUser;

    private sealed class FakeThreeWaVoiceProfileClient(string taskId) : IThreeWaVoiceProfileClient
    {
        private int prepareCount;

        public int PrepareCount => Volatile.Read(ref prepareCount);

        public Task<VoiceProfilePrepareResult> PrepareAsync(
            Stream referenceWav,
            string fileName,
            string profileName,
            string consentType,
            string expectedText,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref prepareCount);
            return Task.FromResult(new VoiceProfilePrepareResult(taskId, "供應商辨識草稿。"));
        }

        public Task<VoiceProfileStatusResult> GetStatusAsync(
            string profileTaskId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VoiceProfileStatusResult("running", false, null, false));

        public Task ConfirmAsync(
            string profileTaskId,
            string transcript,
            CancellationToken cancellationToken) => Task.CompletedTask;

    }

    private sealed class ForUpdateStartedInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM character_profiles", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                Started.TrySetResult(true);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CharacterVoiceCommandInterceptor(bool failCloneInsert = false) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(string commandText)
        {
            Commands.Add(commandText);
            if (failCloneInsert && IsCharacterVoiceInsert(commandText))
            {
                throw new InvalidOperationException("離線模擬第二次 SaveChanges 的 Clone INSERT 失敗。");
            }
        }
    }
}
