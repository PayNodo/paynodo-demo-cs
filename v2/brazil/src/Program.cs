using System.Text.Json;
using PayNodo.Demo;

var rootDir = Path.GetFullPath(".");
PayNodoClient.LoadDotEnv(Path.Combine(rootDir, ".env"));

var command = args.Length > 0 ? args[0] : "sign-payin";
var merchantId = PayNodoClient.Env("PAYNODO_MERCHANT_ID", "replace_with_merchant_id");
var merchantSecret = PayNodoClient.Env("PAYNODO_MERCHANT_SECRET", "replace_with_merchant_secret");

var payIn = PayInPayload(merchantId);
var payOut = PayOutPayload(merchantId);
var status = StatusPayload();
var balance = BalancePayload();

if (command == "verify-callback")
{
    var publicKey = PayNodoClient.ReadPem(
        PayNodoClient.Env(
            "PAYNODO_PLATFORM_PUBLIC_KEY_PEM",
            PayNodoClient.Env("PAYNODO_PLATFORM_PUBLIC_KEY_PATH", Path.Combine(rootDir, "paynodo-public-key.pem"))));
    Print(new
    {
        valid = PayNodoClient.VerifyCallback(
            RequiredEnv("PAYNODO_CALLBACK_BODY"),
            RequiredEnv("PAYNODO_CALLBACK_TIMESTAMP"),
            RequiredEnv("PAYNODO_CALLBACK_SIGNATURE"),
            publicKey)
    });
    return;
}

var privateKey = PayNodoClient.ReadPem(
    PayNodoClient.Env(
        "PAYNODO_PRIVATE_KEY_PEM",
        PayNodoClient.Env("PAYNODO_PRIVATE_KEY_PATH", Path.Combine(rootDir, "merchant-private-key.pem"))));

if (command == "sign-payin")
{
    var timestamp = PayNodoClient.Env("PAYNODO_TIMESTAMP", "2026-04-17T16:20:30-03:00");
    Print(PayNodoClient.SignedHeaders(merchantId, timestamp, merchantSecret, payIn, privateKey));
    return;
}

var client = new PayNodoClient(
    merchantId,
    merchantSecret,
    privateKey,
    PayNodoClient.Env("PAYNODO_BASE_URL", PayNodoClient.DefaultBaseUrl));

ApiResponse response = command switch
{
    "payin" => await client.CreatePayInAsync(payIn),
    "payout" => await client.CreatePayOutAsync(payOut),
    "status" => await client.InquiryStatusAsync(status),
    "balance" => await client.InquiryBalanceAsync(balance),
    "methods" => await client.PaymentMethodsAsync(),
    _ => throw new ArgumentException("Unknown command. Use one of: sign-payin, verify-callback, payin, payout, status, balance, methods")
};

Console.WriteLine($"status={response.Status}");
Console.WriteLine(response.Body);

static string PayInPayload(string merchantId)
{
    return JsonSerializer.Serialize(new
    {
        orderNo = PayNodoClient.Env("PAYNODO_PAYIN_ORDER_NO", "ORDPI2026000001"),
        purpose = PayNodoClient.Env("PAYNODO_PAYIN_PURPOSE", "customer payment"),
        merchant = new
        {
            merchantId,
            merchantName = PayNodoClient.Env("PAYNODO_MERCHANT_NAME", "Integrated Merchant")
        },
        money = new
        {
            currency = "BRL",
            amount = IntEnv("PAYNODO_PAYIN_AMOUNT", 12000)
        },
        payer = new
        {
            pixAccount = PayNodoClient.Env("PAYNODO_PAYER_PIX_ACCOUNT", "48982488880")
        },
        paymentMethod = PayNodoClient.Env("PAYNODO_PAYIN_METHOD", "PIX"),
        expiryPeriod = IntEnv("PAYNODO_EXPIRY_PERIOD", 3600),
        redirectUrl = PayNodoClient.Env("PAYNODO_REDIRECT_URL", "https://merchant.example/return"),
        callbackUrl = PayNodoClient.Env("PAYNODO_CALLBACK_URL", "https://merchant.example/webhooks/paynodo")
    });
}

static string PayOutPayload(string merchantId)
{
    return JsonSerializer.Serialize(new
    {
        additionalParam = new { },
        cashAccount = PayNodoClient.Env("PAYNODO_PAYOUT_CASH_ACCOUNT", "12532481501"),
        receiver = new
        {
            taxNumber = PayNodoClient.Env("PAYNODO_RECEIVER_TAX_NUMBER", "12345678909"),
            accountName = PayNodoClient.Env("PAYNODO_RECEIVER_NAME", "Betty")
        },
        merchant = new
        {
            merchantId
        },
        money = new
        {
            amount = IntEnv("PAYNODO_PAYOUT_AMOUNT", 10000),
            currency = "BRL"
        },
        orderNo = PayNodoClient.Env("PAYNODO_PAYOUT_ORDER_NO", "ORDPO2026000001"),
        paymentMethod = PayNodoClient.Env("PAYNODO_PAYOUT_METHOD", "CPF"),
        purpose = PayNodoClient.Env("PAYNODO_PAYOUT_PURPOSE", "Purpose For Disbursement from API"),
        callbackUrl = PayNodoClient.Env("PAYNODO_CALLBACK_URL", "https://merchant.example/webhooks/paynodo")
    });
}

static string StatusPayload()
{
    return JsonSerializer.Serialize(new
    {
        tradeType = IntEnv("PAYNODO_STATUS_TRADE_TYPE", 1),
        orderNo = PayNodoClient.Env(
            "PAYNODO_STATUS_ORDER_NO",
            PayNodoClient.Env("PAYNODO_PAYIN_ORDER_NO", "ORDPI2026000001"))
    });
}

static string BalancePayload()
{
    return JsonSerializer.Serialize(new
    {
        accountNo = PayNodoClient.Env("PAYNODO_ACCOUNT_NO", "YOUR_ACCOUNT_NO"),
        balanceTypes = new[] { "BALANCE" }
    });
}

static int IntEnv(string key, int fallback)
{
    return int.TryParse(PayNodoClient.Env(key, fallback.ToString()), out var value) ? value : fallback;
}

static string RequiredEnv(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"{key} is required");
    }
    return value;
}

static void Print(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
