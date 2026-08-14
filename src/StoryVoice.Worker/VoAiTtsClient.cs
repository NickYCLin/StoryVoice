using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StoryVoice.Worker;

public sealed class VoAiTtsClient : IVoAiTtsClient
{
    public const string HttpClientName = "VoAi";

    private const int WavHeaderLength = 12;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VoAiOptions _options;

    public VoAiTtsClient(IHttpClientFactory httpClientFactory, IOptions<VoAiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task SynthesizeWavAsync(
        VoAiSpeechSynthesisRequest request,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);
        ValidateRequest(request);
        var endpoint = ValidateConfiguration();
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The VoAI destination stream must be writable.", nameof(destination));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new SpeechRequestBody(
                request.Model,
                request.Text,
                request.Speaker,
                request.Style,
                request.Speed,
                request.PitchShift,
                StyleWeight: 0,
                BreathPause: 0))
        };
        message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey.Trim());
        message.Headers.TryAddWithoutValidation("x-output-format", "wav");
        message.Headers.TryAddWithoutValidation(
            "x-sample-rate",
            _options.SampleRate.ToString(CultureInfo.InvariantCulture));

        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "VoAI speech synthesis failed.",
                inner: null,
                response.StatusCode);
        }

        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength > _options.MaximumResponseBytes)
        {
            throw new InvalidDataException("VoAI returned an audio response larger than the configured limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        var header = new byte[WavHeaderLength];
        var headerBytes = await ReadHeaderAsync(source, header, timeout.Token);
        if (headerBytes != WavHeaderLength || !IsWavHeader(header))
        {
            throw new InvalidDataException("VoAI returned an invalid WAV response.");
        }

        await destination.WriteAsync(header, timeout.Token);
        var totalBytes = (long)header.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, timeout.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > _options.MaximumResponseBytes)
                {
                    throw new InvalidDataException("VoAI returned an audio response larger than the configured limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Uri ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("VoAI API key is not configured.");
        }

        if (_options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("VoAI timeout must be greater than zero.");
        }

        if (_options.MaximumResponseBytes < WavHeaderLength)
        {
            throw new InvalidOperationException("VoAI response limit is too small for a WAV response.");
        }

        if (_options.SampleRate != 32_000)
        {
            throw new InvalidOperationException("This VoAI provider contract requires a 32000 Hz WAV response.");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(baseUri.Host, "connect.voai.ai", StringComparison.OrdinalIgnoreCase)
            || !baseUri.IsDefaultPort)
        {
            throw new InvalidOperationException(
                "VoAI base URL must use the official HTTPS origin https://connect.voai.ai.");
        }

        var normalizedBaseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "TTS/Speech");
    }

    private static void ValidateRequest(VoAiSpeechSynthesisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1_000)
        {
            throw new ArgumentException("VoAI text must contain between 1 and 1000 characters.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Model)
            || string.IsNullOrWhiteSpace(request.Speaker)
            || string.IsNullOrWhiteSpace(request.Style))
        {
            throw new ArgumentException("VoAI model, speaker, and style are required.", nameof(request));
        }

        if (request.Speed is < 0.5 or > 1.5)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "VoAI speed must be between 0.5 and 1.5.");
        }

        if (request.PitchShift is < -5 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "VoAI pitch shift must be between -5 and 5.");
        }
    }

    private static async Task<int> ReadHeaderAsync(
        Stream source,
        byte[] header,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < header.Length)
        {
            var bytesRead = await source.ReadAsync(header.AsMemory(offset), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            offset += bytesRead;
        }

        return offset;
    }

    private static bool IsWavHeader(ReadOnlySpan<byte> header) =>
        header.Length >= WavHeaderLength
        && header[0] == (byte)'R'
        && header[1] == (byte)'I'
        && header[2] == (byte)'F'
        && header[3] == (byte)'F'
        && header[8] == (byte)'W'
        && header[9] == (byte)'A'
        && header[10] == (byte)'V'
        && header[11] == (byte)'E';

    private sealed record SpeechRequestBody(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("speaker")] string Speaker,
        [property: JsonPropertyName("style")] string Style,
        [property: JsonPropertyName("speed")] double Speed,
        [property: JsonPropertyName("pitch_shift")] int PitchShift,
        [property: JsonPropertyName("style_weight")] double StyleWeight,
        [property: JsonPropertyName("breath_pause")] double BreathPause);
}
