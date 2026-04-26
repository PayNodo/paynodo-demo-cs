# PayNodo Brazil V2 C# Demo

Backend-only C# demo for PayNodo Brazil V2.

## Requirements

- .NET 8 SDK+

No external NuGet packages are required.

## Setup

```shell
cp .env.example .env
```

Edit `.env` and replace sandbox values with the credentials from the merchant cabinet.
Save the merchant private key as `merchant-private-key.pem`, or set `PAYNODO_PRIVATE_KEY_PEM` directly in `.env`.

## Generate a signed PayIn preview

```shell
dotnet run --project src/PayNodo.Demo.csproj -- sign-payin
```

## Send sandbox requests

```shell
dotnet run --project src/PayNodo.Demo.csproj -- payin
dotnet run --project src/PayNodo.Demo.csproj -- payout
dotnet run --project src/PayNodo.Demo.csproj -- status
dotnet run --project src/PayNodo.Demo.csproj -- balance
dotnet run --project src/PayNodo.Demo.csproj -- methods
```

## Verify a callback signature

```shell
PAYNODO_CALLBACK_BODY='{"orderNo":"ORDPI2026000001","status":"SUCCESS"}' \
PAYNODO_CALLBACK_TIMESTAMP='2026-04-17T13:25:10.000Z' \
PAYNODO_CALLBACK_SIGNATURE='replace_with_callback_signature' \
dotnet run --project src/PayNodo.Demo.csproj -- verify-callback
```

The private key and merchant secret must stay on the merchant backend.
