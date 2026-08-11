using System.Text;
using __PROJECT_NAMESPACE__.Operations;

var failures = new List<string>();

await ValidateAtomicMiddleOperatorFailureRecoveryAsync(failures);
ValidateIncompleteMaterialDiagnostic(failures);
await ValidateSensitiveShareRequestsAsync(failures);

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"FAIL: {failure}");
    }
    return 1;
}

Console.WriteLine(
    "PASS: atomic multi-passphrase initialization recovery and diagnostics passed; unseal, rekey-update, and rekey-verification requests emitted correct JSON without converting shares to strings.");
return 0;

static async Task ValidateSensitiveShareRequestsAsync(
    ICollection<string> failures)
{
    var handler = new RecordingOpenBaoHandler();
    using var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri("http://openbao.test/")
    };
    using var client = new OpenBaoSystemClient(httpClient);
    var share = Encoding.UTF8.GetBytes("AQID+/==");
    const string nonce = "nonce-\"-\\-\n";

    try
    {
        await client.SubmitUnsealShareAsync(share);
        await client.SubmitRekeyShareAsync(share, nonce);
        var verificationComplete =
            await client.SubmitRekeyVerificationShareAsync(share, nonce);

        if (!verificationComplete
            || handler.Requests.Count != 3
            || !RequestMatches(
                handler.Requests[0],
                "v1/sys/unseal",
                "AQID+/==",
                expectedNonce: null)
            || !RequestMatches(
                handler.Requests[1],
                "v1/sys/rotate/root/update",
                "AQID+/==",
                nonce)
            || !RequestMatches(
                handler.Requests[2],
                "v1/sys/rotate/root/verify",
                "AQID+/==",
                nonce))
        {
            failures.Add(
                "The sensitive OpenBao request writer did not emit the expected unseal/rekey JSON payloads and endpoints.");
        }
    }
    finally
    {
        Array.Clear(share, 0, share.Length);
        foreach (var request in handler.Requests)
        {
            Array.Clear(request.Body, 0, request.Body.Length);
        }
    }
}

static bool RequestMatches(
    RecordedRequest request,
    string expectedPath,
    string expectedShare,
    string? expectedNonce)
{
    if (request.Path != expectedPath
        || request.ContentType != "application/json")
    {
        return false;
    }

    using var document = System.Text.Json.JsonDocument.Parse(request.Body);
    var root = document.RootElement;
    return root.GetProperty("key").GetString() == expectedShare
        && (expectedNonce is null
            ? !root.TryGetProperty("nonce", out _)
            : root.GetProperty("nonce").GetString() == expectedNonce);
}

static async Task ValidateAtomicMiddleOperatorFailureRecoveryAsync(
    ICollection<string> failures)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"agency-multi-init-validation-{Guid.NewGuid():N}");
    var inputs = new Queue<byte[]>();
    EnqueuePair(inputs, "operator-one");
    EnqueuePair(inputs, "operator-two");
    inputs.Enqueue(Encoding.UTF8.GetBytes("operator-three-first"));
    inputs.Enqueue(Encoding.UTF8.GetBytes("operator-three-typo"));
    EnqueuePair(inputs, "operator-three-correct");
    EnqueuePair(inputs, "operator-four");
    EnqueuePair(inputs, "operator-five");
    var messages = new List<string>();
    var failedOperatorThreeWrite = false;
    var initializeObservedAllInputsCaptured = false;
    var client = new FakeOpenBaoSystemClient(
        onInitialize: () =>
        {
            initializeObservedAllInputsCaptured = inputs.Count == 0;
        });
    var provider = new MultiPassphraseUnsealProvider(
        directory,
        async (path, share, passphrase, overwrite, cancellationToken) =>
        {
            if (!failedOperatorThreeWrite
                && path.Contains(".init-staging", StringComparison.Ordinal)
                && path.EndsWith("unseal-share-03.v1.json", StringComparison.Ordinal))
            {
                failedOperatorThreeWrite = true;
                throw new IOException("simulated transient operator-3 staging failure");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                "encrypted-test-bundle",
                cancellationToken);
        },
        _ => inputs.Dequeue(),
        messages.Add);

    try
    {
        var result = await provider.InitializeIfNeededAsync(client);
        var finalBundles = Directory.GetFiles(
            directory,
            "unseal-share-*.v1.json",
            SearchOption.TopDirectoryOnly);
        var diagnostic = provider.GetMaterialDiagnostic(
            await client.GetStatusAsync());

        if (result is null
            || client.InitializeCalls != 1
            || !initializeObservedAllInputsCaptured
            || messages.Count != 1
            || !failedOperatorThreeWrite
            || client.SubmittedShares != 3
            || client.IsSealed
            || finalBundles.Length != 5
            || Directory.Exists(Path.Combine(directory, ".init-staging"))
            || !diagnostic.IsUsable)
        {
            failures.Add(
                "The simulated middle-operator failure did not finish with all passphrases captured before native init, five final bundles, and an unsealed 3-of-5 fake OpenBao instance.");
        }
    }
    finally
    {
        while (inputs.TryDequeue(out var remaining))
        {
            Array.Clear(remaining, 0, remaining.Length);
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void ValidateIncompleteMaterialDiagnostic(
    ICollection<string> failures)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"agency-multi-diagnostic-validation-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        for (var number = 1; number <= 2; number++)
        {
            File.WriteAllText(
                Path.Combine(directory, $"unseal-share-{number:D2}.v1.json"),
                "encrypted-placeholder");
        }

        var provider = new MultiPassphraseUnsealProvider(
            directory,
            static (_, _, _, _, _) => Task.CompletedTask);
        var diagnostic = provider.GetMaterialDiagnostic(
            new OpenBaoSealStatus(
                Initialized: true,
                Sealed: true,
                Shares: 5,
                Threshold: 3,
                Progress: 0,
                SealType: "shamir"));

        if (diagnostic.IsUsable
            || !diagnostic.Message.Contains(
                "3 of 5 expected unseal-share bundles are missing",
                StringComparison.Ordinal))
        {
            failures.Add(
                "Initialized OpenBao with only 2-of-5 bundles did not report the actionable below-threshold diagnostic.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void EnqueuePair(Queue<byte[]> target, string value)
{
    target.Enqueue(Encoding.UTF8.GetBytes(value));
    target.Enqueue(Encoding.UTF8.GetBytes(value));
}

internal sealed class FakeOpenBaoSystemClient(
    Action onInitialize) : IOpenBaoSystemClient
{
    public int InitializeCalls { get; private set; }
    public int SubmittedShares { get; private set; }
    public bool IsSealed { get; private set; } = true;
    private bool _initialized;

    public Task<OpenBaoSealStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OpenBaoSealStatus(
            _initialized,
            IsSealed,
            5,
            3,
            SubmittedShares,
            "shamir"));

    public Task<OpenBaoInitializationResult> InitializeAsync(
        int shares,
        int threshold,
        bool autoSeal,
        CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        onInitialize();
        _initialized = true;
        return Task.FromResult(new OpenBaoInitializationResult(
            Enumerable.Range(1, shares)
                .Select(number => Encoding.UTF8.GetBytes($"plaintext-share-{number}"))
                .ToArray(),
            "root-token"));
    }

    public Task<OpenBaoSealStatus> SubmitUnsealShareAsync(
        ReadOnlyMemory<byte> share,
        bool reset = false,
        CancellationToken cancellationToken = default)
    {
        SubmittedShares = reset ? 0 : SubmittedShares + 1;
        IsSealed = SubmittedShares < 3;
        return GetStatusAsync(cancellationToken);
    }

    public Task<string> BeginRekeyAsync(
        int shares,
        int threshold,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<OpenBaoRekeyProgress> SubmitRekeyShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> SubmitRekeyVerificationShareAsync(
        ReadOnlyMemory<byte> share,
        string nonce,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class RecordingOpenBaoHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery.TrimStart('/')
            ?? throw new InvalidOperationException("The request URI is missing.");
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            path,
            request.Content?.Headers.ContentType?.MediaType,
            body));

        var responseJson = path switch
        {
            "v1/sys/unseal" =>
                "{\"initialized\":true,\"sealed\":false,\"n\":5,\"t\":3,\"progress\":0,\"type\":\"shamir\"}",
            "v1/sys/rotate/root/update" =>
                "{\"complete\":false,\"verification_required\":true,\"nonce\":\"server-nonce\",\"required\":1,\"keys_base64\":[]}",
            "v1/sys/rotate/root/verify" =>
                "{\"complete\":true,\"verification_required\":false,\"nonce\":\"server-nonce\",\"required\":0,\"keys_base64\":[]}",
            _ => throw new InvalidOperationException($"Unexpected request path '{path}'.")
        };

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseJson,
                Encoding.UTF8,
                "application/json")
        };
    }
}

internal sealed record RecordedRequest(
    string Path,
    string? ContentType,
    byte[] Body);
