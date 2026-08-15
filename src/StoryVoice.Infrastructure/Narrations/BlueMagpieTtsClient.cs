using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;

namespace StoryVoice.Infrastructure.Narrations;

public sealed class BlueMagpieTtsClient(
    HttpClient httpClient,
    IOptions<BlueMagpieOptions> options) : IBlueMagpieTtsClient
{
    private const string TokenHeader = "X-StoryVoice-Internal-Token";
    private const string RevisionHeader = "X-BlueMagpie-Model-Revision";
    private const string ProviderVersionHeader = "X-BlueMagpie-Provider-Version";
    private const string VoiceHeader = "X-BlueMagpie-Voice";

    public async Task<BlueMagpieSynthesisResult> SynthesizeAsync(
        string text,
        string voice,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech")
        {
            Content = JsonContent.Create(new BlueMagpieSpeechRequest(text, voice)),
        };
        request.Headers.TryAddWithoutValidation(TokenHeader, options.Value.InternalToken);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new SeriesVoicePreviewUnavailableException(
                    ClassifyFailure(response.StatusCode));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "audio/wav", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mediaType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            {
                throw ContractViolation();
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is <= 0 || contentLength > options.Value.MaximumResponseBytes)
            {
                throw ContractViolation();
            }

            var revision = ReadSingleHeader(response, RevisionHeader);
            var providerVersion = ReadSingleHeader(response, ProviderVersionHeader);
            var returnedVoice = ReadSingleHeader(response, VoiceHeader);
            if (!string.Equals(revision, options.Value.ModelRevision, StringComparison.Ordinal)
                || !string.Equals(
                    providerVersion,
                    BlueMagpieOptions.PinnedProviderVersion,
                    StringComparison.Ordinal)
                || !string.Equals(returnedVoice, voice, StringComparison.Ordinal))
            {
                throw ContractViolation();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var content = await ReadBoundedAsync(
                responseStream,
                options.Value.MaximumResponseBytes,
                cancellationToken);
            if (!BlueMagpiePcmWaveValidator.IsValid(content))
            {
                throw ContractViolation();
            }

            return new BlueMagpieSynthesisResult(
                content,
                "audio/wav",
                revision,
                providerVersion,
                returnedVoice);
        }
        catch (SeriesVoicePreviewUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SeriesVoicePreviewUnavailableException();
        }
        catch (HttpRequestException exception)
        {
            throw new SeriesVoicePreviewUnavailableException(exception);
        }
        catch (IOException exception)
        {
            throw new SeriesVoicePreviewUnavailableException(exception);
        }
    }

    private static string ReadSingleHeader(HttpResponseMessage response, string headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values))
        {
            return string.Empty;
        }

        var candidates = values.Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : string.Empty;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maximumBytes)
                {
                    throw ContractViolation();
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static SeriesVoicePreviewFailureKind ClassifyFailure(HttpStatusCode statusCode) =>
        (int)statusCode is 408 or 425 or 429 or 500 or 502 or 503 or 504
            ? SeriesVoicePreviewFailureKind.Unavailable
            : SeriesVoicePreviewFailureKind.ContractViolation;

    private static SeriesVoicePreviewUnavailableException ContractViolation() =>
        new(SeriesVoicePreviewFailureKind.ContractViolation);

    private sealed record BlueMagpieSpeechRequest(string Text, string Voice);
}
