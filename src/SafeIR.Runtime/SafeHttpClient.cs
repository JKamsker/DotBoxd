namespace SafeIR.Runtime;

using System.Globalization;
using System.Net;
using System.Text;
using SafeIR;

public static class SafeHttpClient
{
    public static async ValueTask<string> GetTextAsync(
        SandboxContext context,
        SandboxUri uri,
        HttpMessageInvoker invoker,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var resource = SanitizeForAudit(uri.Value);
        try {
            var request = ResolveRequest(context, uri);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
            using var response = await invoker.SendAsync(message, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var text = await ReadLimitedTextAsync(context, response, request.MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            Audit(context, startedAt, true, resource, Encoding.UTF8.GetByteCount(text), null);
            return text;
        }
        catch (SandboxRuntimeException ex) {
            Audit(context, startedAt, false, resource, null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            var error = new SandboxError(SandboxErrorCode.Timeout, "net.http.get denied: request timed out");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "net.http.get cancelled");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "net.http.get failed");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static SafeHttpRequest ResolveRequest(SandboxContext context, SandboxUri sandboxUri)
    {
        context.RequireCapability("net.http.get");
        var grant = context.GetCapability("net.http.get");
        if (!Uri.TryCreate(sandboxUri.Value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: URI must be absolute");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: user info is not allowed");
        }

        RequireAllowedScheme(grant, uri);
        RequireAllowedHost(grant, uri);
        RequireIpPolicy(grant, uri.Host);
        return new SafeHttpRequest(
            uri,
            ReadLong(grant, "maxResponseBytes", context.Budget.Limits.MaxNetworkBytesRead),
            TimeSpan.FromMilliseconds(ReadLong(grant, "timeoutMs", 2_000)));
    }

    private static async ValueTask<string> ReadLimitedTextAsync(
        SandboxContext context,
        HttpResponseMessage response,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes) {
            throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true) {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) {
                break;
            }

            context.Budget.ChargeNetworkRead(read);
            if (memory.Length + read > maxBytes) {
                throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
            }

            memory.Write(buffer, 0, read);
        }

        context.ChargeFuel(75 + memory.Length);
        context.ChargeAllocation(memory.Length);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static void RequireAllowedScheme(CapabilityGrant grant, Uri uri)
    {
        var allowed = ReadSet(grant, "allowedSchemes", ["https"]);
        if (!allowed.Contains(uri.Scheme)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: scheme is not allowed");
        }
    }

    private static void RequireAllowedHost(CapabilityGrant grant, Uri uri)
    {
        var allowed = ReadSet(grant, "allowedHosts", []);
        if (allowed.Count == 0 || !allowed.Contains(uri.Host)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: host is not allowed");
        }
    }

    private static void RequireIpPolicy(CapabilityGrant grant, string host)
    {
        if (!IPAddress.TryParse(host, out var address)) {
            return;
        }

        if (!ReadBool(grant, "allowIpLiterals")) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: IP literals are not allowed");
        }

        if (!ReadBool(grant, "allowPrivateNetwork") && IsPrivateOrLoopback(address)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (
            bytes[0] == 10 ||
            bytes[0] == 127 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 169 && bytes[1] == 254);
    }

    private static HashSet<string> ReadSet(CapabilityGrant grant, string key, string[] fallback)
    {
        var text = grant.Parameters.TryGetValue(key, out var value) ? value : string.Join(',', fallback);
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value) &&
           bool.TryParse(value, out var parsed) &&
           parsed;

    private static long ReadLong(CapabilityGrant grant, string key, long fallback)
        => grant.Parameters.TryGetValue(key, out var value) &&
           long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static void Audit(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: "net.http.get",
            CapabilityId: "net.http.get",
            Effect: SandboxEffect.Network,
            ResourceId: resource,
            ErrorCode: error,
            Bytes: bytes));

    private static string SanitizeForAudit(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : "invalid-uri";

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record SafeHttpRequest(Uri Uri, long MaxResponseBytes, TimeSpan Timeout);
}
