using System.Net;
using System.Text;
using __PROJECT_NAMESPACE__.Operations;

var handler = new TransientTransitMountHandler();
using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("http://openbao.test/")
};
var options = new OpenBaoToolOptions(
    httpClient.BaseAddress,
    "transit",
    "validation-jwt",
    "database",
    "validation-postgresql",
    "validation-runtime",
    "validation-migrator",
    "validation-runtime-policy",
    "/tmp/runtime-token",
    "validation-migrator-policy",
    "/tmp/migrator-token",
    "validation-jwt-rotation-policy",
    "/tmp/jwt-rotation-token",
    "validation-database-rotation-policy",
    "/tmp/database-rotation-token",
    "/tmp/provisioning-token",
    "validation-bootstrap-token",
    TimeSpan.FromSeconds(5));
var client = new OpenBaoTransitAdminClient(httpClient, options);

await client.EnsureTransitEnabledAsync(CancellationToken.None);

if (handler.MountListRequests != 2
    || handler.MountEnableRequests != 2)
{
    Console.Error.WriteLine(
        $"FAIL: expected two mount-list and two mount-enable requests, observed {handler.MountListRequests} and {handler.MountEnableRequests}.");
    return 1;
}

var fatalHandler = new FatalTransitMountHandler();
using (var fatalHttpClient = new HttpClient(fatalHandler)
{
    BaseAddress = new Uri("http://openbao.test/")
})
{
    var fatalClient = new OpenBaoTransitAdminClient(
        fatalHttpClient,
        options);
    try
    {
        await fatalClient.EnsureTransitEnabledAsync(
            CancellationToken.None);
        Console.Error.WriteLine(
            "FAIL: a permission failure was unexpectedly retried or accepted.");
        return 1;
    }
    catch (InvalidOperationException)
    {
        if (fatalHandler.MountEnableRequests != 1)
        {
            Console.Error.WriteLine(
                "FAIL: a non-transient permission failure was retried.");
            return 1;
        }
    }
}

var stateDirectory = Path.Combine(
    Path.GetTempPath(),
    $"agency-passphrase-provisioning-validation-{Guid.NewGuid():N}");
Directory.CreateDirectory(stateDirectory);
try
{
    var diagnosticOptions = options with
    {
        RuntimeTokenFile = Path.Combine(stateDirectory, "runtime-token"),
        MigratorTokenFile = Path.Combine(stateDirectory, "migrator-token"),
        JwtRotationTokenFile = Path.Combine(stateDirectory, "jwt-rotation-token"),
        DatabaseRotationTokenFile = Path.Combine(stateDirectory, "database-rotation-token"),
        ProvisioningTokenFile = Path.Combine(stateDirectory, ".openbao-provisioning-token")
    };
    File.WriteAllText(
        diagnosticOptions.ProvisioningTokenFile,
        "short-lived-resume-token");
    var incomplete = JwtKeyStore.GetProvisioningDiagnostic(
        stateDirectory,
        diagnosticOptions);
    if (incomplete.IsComplete
        || !incomplete.Message.Contains(
            "run 'auth init' to resume",
            StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            "FAIL: interrupted provisioning was not diagnosed as automatically resumable.");
        return 1;
    }

    foreach (var path in new[]
    {
        Path.Combine(stateDirectory, "auth-jwt-key-ring.json"),
        diagnosticOptions.RuntimeTokenFile,
        diagnosticOptions.MigratorTokenFile,
        diagnosticOptions.JwtRotationTokenFile,
        diagnosticOptions.DatabaseRotationTokenFile,
        Path.Combine(stateDirectory, ".auth-provisioning.complete")
    })
    {
        File.WriteAllText(path, "present");
    }

    var complete = JwtKeyStore.GetProvisioningDiagnostic(
        stateDirectory,
        diagnosticOptions);
    if (!complete.IsComplete)
    {
        Console.Error.WriteLine(
            "FAIL: completed local provisioning artifacts were not diagnosed as complete.");
        return 1;
    }
}
finally
{
    Directory.Delete(stateDirectory, recursive: true);
}

Console.WriteLine(
    "PASS: auth provisioning retried a transient Transit mount failure, failed fast on permission denial, and status distinguished interrupted-resumable state from complete state.");
return 0;

internal sealed class TransientTransitMountHandler : HttpMessageHandler
{
    public int MountListRequests { get; private set; }
    public int MountEnableRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery;
        if (request.Method == HttpMethod.Get
            && path == "/v1/sys/mounts")
        {
            MountListRequests++;
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                "{\"data\":{}}"));
        }

        if (request.Method == HttpMethod.Post
            && path == "/v1/sys/mounts/transit")
        {
            MountEnableRequests++;
            return Task.FromResult(
                MountEnableRequests == 1
                    ? JsonResponse(
                        HttpStatusCode.InternalServerError,
                        "{\"errors\":[\"cannot write to readonly storage\"]}")
                    : JsonResponse(
                        HttpStatusCode.NoContent,
                        string.Empty));
        }

        throw new InvalidOperationException(
            $"Unexpected validation request: {request.Method} {path}.");
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json) =>
        new(statusCode)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
}

internal sealed class FatalTransitMountHandler : HttpMessageHandler
{
    public int MountEnableRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":{}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }

        MountEnableRequests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                "{\"errors\":[\"permission denied\"]}",
                Encoding.UTF8,
                "application/json")
        });
    }
}
