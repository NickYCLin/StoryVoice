using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;

namespace StoryVoice.Infrastructure.Narrations;

public sealed class LocalCloneGatewayClient(
    HttpClient httpClient,
    IOptions<LocalClonePreviewOptions> options) : ILocalCloneGatewayClient
{
    private const string TokenHeader = "X-StoryVoice-Internal-Token";
    private const string SourceRevisionHeader = "X-CosyVoice-Source-Revision";
    private const string ModelIdHeader = "X-CosyVoice-Model-Id";
    private const string ModelRevisionHeader = "X-CosyVoice-Model-Revision";

    public async Task<LocalCloneGatewayAudio> SynthesizeAsync(
        LocalCloneGatewayRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(request.Text), "text");
        multipart.Add(new StringContent(request.ReferenceTranscript), "reference_text");
        var audio = new ByteArrayContent(request.ReferenceAudio);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        multipart.Add(audio, "reference_audio", "reference.wav");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/voice-clone/speech")
        {
            Content = multipart,
        };
        httpRequest.Headers.TryAddWithoutValidation(TokenHeader, options.Value.InternalToken);

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw FailureFor(response.StatusCode);
            }

            ValidateResponseHeaders(response);
            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength is null
                || declaredLength <= 0
                || declaredLength > options.Value.MaximumResponseBytes)
            {
                throw ContractInvalid();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var content = await ReadBoundedAsync(
                responseStream,
                options.Value.MaximumResponseBytes,
                cancellationToken);
            if (content.LongLength != declaredLength)
            {
                throw ContractInvalid();
            }

            try
            {
                LocalClonePcmWaveValidator.ValidateOutput(content, options.Value.MaximumResponseBytes);
            }
            catch (InvalidDataException exception)
            {
                throw ContractInvalid(exception);
            }

            return new LocalCloneGatewayAudio(content, "audio/wav");
        }
        catch (LocalClonePreviewUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.GatewayUnavailable);
        }
        catch (HttpRequestException exception)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.GatewayUnavailable,
                exception);
        }
        catch (IOException exception)
        {
            throw new LocalClonePreviewUnavailableException(
                LocalClonePreviewFailureKind.GatewayUnavailable,
                exception);
        }
    }

    private static void ValidateResponseHeaders(HttpResponseMessage response)
    {
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "audio/wav",
                StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentEncoding.Count != 0
            || response.Headers.CacheControl?.NoStore != true
            || !HasSingleExactHeader(
                response,
                SourceRevisionHeader,
                LocalClonePreviewOptions.PinnedCosyVoiceSourceRevision)
            || !HasSingleExactHeader(
                response,
                ModelIdHeader,
                LocalClonePreviewOptions.PinnedModelId)
            || !HasSingleExactHeader(
                response,
                ModelRevisionHeader,
                LocalClonePreviewOptions.PinnedModelRevision))
        {
            throw ContractInvalid();
        }
    }

    private static bool HasSingleExactHeader(
        HttpResponseMessage response,
        string name,
        string expected)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return false;
        }

        var candidates = values.Take(2).ToArray();
        return candidates.Length == 1
            && string.Equals(candidates[0], expected, StringComparison.Ordinal);
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
                    return output.ToArray();
                }

                if (output.Length > maximumBytes - read)
                {
                    throw ContractInvalid();
                }

                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static LocalClonePreviewUnavailableException FailureFor(HttpStatusCode statusCode) =>
        new((int)statusCode is 408 or 425 or 429 or >= 500
            ? LocalClonePreviewFailureKind.GatewayUnavailable
            : LocalClonePreviewFailureKind.GatewayContractInvalid);

    private static LocalClonePreviewUnavailableException ContractInvalid(Exception? inner = null) =>
        new(LocalClonePreviewFailureKind.GatewayContractInvalid, inner);
}
