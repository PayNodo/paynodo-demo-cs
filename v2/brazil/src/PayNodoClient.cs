using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PayNodo.Demo;

public sealed class PayNodoClient
{
    public const string DefaultBaseUrl = "https://sandbox-api.paynodo.com";

    private readonly string _baseUrl;
    private readonly string _merchantId;
    private readonly string _merchantSecret;
    private readonly string _privateKeyPem;
    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _now;

    public PayNodoClient(
        string merchantId,
        string merchantSecret,
        string privateKeyPem,
        string baseUrl = DefaultBaseUrl,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? now = null)
    {
        if (string.IsNullOrWhiteSpace(merchantId)) throw new ArgumentException("merchantId is required");
        if (string.IsNullOrWhiteSpace(merchantSecret)) throw new ArgumentException("merchantSecret is required");
        if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("privateKeyPem is required");

        _merchantId = merchantId;
        _merchantSecret = merchantSecret;
        _privateKeyPem = privateKeyPem;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static void LoadDotEnv(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;

            var index = trimmed.IndexOf('=');
            var key = trimmed[..index].Trim();
            var value = trimmed[(index + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0 && Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public static string Env(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string ReadPem(string valueOrPath)
    {
        if (string.IsNullOrWhiteSpace(valueOrPath)) throw new ArgumentException("Missing PEM value or path");
        return valueOrPath.Contains("-----BEGIN", StringComparison.Ordinal)
            ? valueOrPath.Replace("\\n", "\n", StringComparison.Ordinal)
            : File.ReadAllText(valueOrPath);
    }

    public static string MinifyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    public static string BuildStringToSign(string timestamp, string merchantSecret, string jsonBody)
    {
        return string.Join("|", timestamp, merchantSecret, MinifyJson(jsonBody));
    }

    public static SignedPayload SignPayload(string timestamp, string merchantSecret, string jsonBody, string privateKeyPem)
    {
        var stringToSign = BuildStringToSign(timestamp, merchantSecret, jsonBody);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(stringToSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return new SignedPayload(
            Convert.ToBase64String(signature),
            stringToSign,
            MinifyJson(jsonBody));
    }

    public static SignedRequest SignedHeaders(
        string merchantId,
        string timestamp,
        string merchantSecret,
        string jsonBody,
        string privateKeyPem)
    {
        var signed = SignPayload(timestamp, merchantSecret, jsonBody, privateKeyPem);
        return new SignedRequest(
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-PARTNER-ID"] = merchantId,
                ["X-TIMESTAMP"] = timestamp,
                ["X-SIGNATURE"] = signed.Signature
            },
            signed.Body,
            signed.StringToSign);
    }

    public static bool VerifyCallback(string rawBody, string timestamp, string signature, string platformPublicKeyPem)
    {
        var stringToVerify = string.Join("|", timestamp, MinifyJson(rawBody));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(platformPublicKeyPem);
        return rsa.VerifyData(
            Encoding.UTF8.GetBytes(stringToVerify),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    public async Task<ApiResponse> RequestAsync(string method, string endpoint, string jsonBody)
    {
        var normalizedMethod = method.ToUpperInvariant();
        var signatureBody = normalizedMethod == "GET" ? "{}" : jsonBody;
        var signed = SignedHeaders(
            _merchantId,
            _now().UtcDateTime.ToString("O"),
            _merchantSecret,
            signatureBody,
            _privateKeyPem);

        using var request = new HttpRequestMessage(new HttpMethod(normalizedMethod), _baseUrl + endpoint);
        foreach (var header in signed.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (normalizedMethod != "GET")
        {
            request.Content = new StringContent(signed.Body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        using var response = await _httpClient.SendAsync(request);
        return new ApiResponse(
            (int) response.StatusCode,
            response.Headers.ToDictionary(item => item.Key, item => item.Value.ToArray()),
            await response.Content.ReadAsStringAsync());
    }

    public Task<ApiResponse> CreatePayInAsync(string jsonBody) => RequestAsync("POST", "/v2.0/transaction/pay-in", jsonBody);

    public Task<ApiResponse> CreatePayOutAsync(string jsonBody) => RequestAsync("POST", "/v2.0/disbursement/pay-out", jsonBody);

    public Task<ApiResponse> InquiryStatusAsync(string jsonBody) => RequestAsync("POST", "/v2.0/inquiry-status", jsonBody);

    public Task<ApiResponse> InquiryBalanceAsync(string jsonBody) => RequestAsync("POST", "/v2.0/inquiry-balance", jsonBody);

    public Task<ApiResponse> PaymentMethodsAsync() => RequestAsync("GET", "/v2.0/payment-methods", "{}");
}

public sealed record SignedPayload(string Signature, string StringToSign, string Body);

public sealed record SignedRequest(IReadOnlyDictionary<string, string> Headers, string Body, string StringToSign);

public sealed record ApiResponse(int Status, IReadOnlyDictionary<string, string[]> Headers, string Body);
