namespace SafeIR.Runtime;

using System.Globalization;
using System.Net;
using System.Text;
using SafeIR;

public delegate ValueTask<IReadOnlyList<IPAddress>> SafeDnsResolver(string host, CancellationToken cancellationToken);

public static class SafeHttpClient
{
    public static async ValueTask<string> GetTextAsync(
        SandboxContext context,
        SandboxUri uri,
        HttpMessageInvoker? invoker,
        SafeDnsResolver? dnsResolver,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var resource = SanitizeForAudit(uri.Value);
        try {
            var request = ResolveRequest(context, uri);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);
            var addresses = await ResolveVettedAddressesAsync(
                    request.Grant,
                    request.Uri.Host,
                    dnsResolver ?? ResolveDnsAsync,
                    timeout.Token)
                .ConfigureAwait(false);
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
            using var response = await SafePinnedHttpTransport.SendAsync(invoker, message, addresses, timeout.Token)
                .ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } finalUri && !SameUri(finalUri, request.Uri)) {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

            if (IsRedirect(response.StatusCode)) {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

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
        return new SafeHttpRequest(
            grant,
            uri,
            ReadLong(grant, "maxResponseBytes", context.Budget.Limits.MaxNetworkBytesRead),
            ReadTimeout(grant));
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
        var text = Encoding.UTF8.GetString(memory.ToArray());
        context.ChargeString(text);
        return text;
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
        if (allowed.Count == 0 || !allowed.Any(host => MatchesAllowedAuthority(host, uri))) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: host is not allowed");
        }
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveVettedAddressesAsync(
        CapabilityGrant grant,
        string host,
        SafeDnsResolver dnsResolver,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address)) {
            RequireIpLiteralAllowed(grant, address);
            return [address];
        }

        var allowPrivateNetwork = ReadBool(grant, "allowPrivateNetwork");
        var addresses = await dnsResolver(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0 ||
            !allowPrivateNetwork && addresses.Any(SafeIpAddressClassifier.IsNonGlobal)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }

        return addresses;
    }

    private static void RequireIpLiteralAllowed(CapabilityGrant grant, IPAddress address)
    {
        if (!ReadBool(grant, "allowIpLiterals")) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: IP literals are not allowed");
        }

        if (!ReadBool(grant, "allowPrivateNetwork") && SafeIpAddressClassifier.IsNonGlobal(address)) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MultipleChoices or
            HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static TimeSpan ReadTimeout(CapabilityGrant grant)
    {
        var milliseconds = ReadLong(grant, "timeoutMs", 2_000);
        if (milliseconds <= 0 || milliseconds > 60_000) {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: timeout is outside the allowed range");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveDnsAsync(
        string host,
        CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

    private static HashSet<string> ReadSet(CapabilityGrant grant, string key, string[] fallback)
    {
        var text = grant.Parameters.TryGetValue(key, out var value) ? value : string.Join(',', fallback);
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return false;
        }

        if (!bool.TryParse(value, out var parsed)) {
            throw Error(SandboxErrorCode.PermissionDenied, $"net.http.get denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static long ReadLong(CapabilityGrant grant, string key, long fallback)
    {
        if (!grant.Parameters.TryGetValue(key, out var value)) {
            return fallback;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0) {
            throw Error(SandboxErrorCode.PermissionDenied, $"net.http.get denied: parameter '{key}' is invalid");
        }

        return parsed;
    }

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
            ? $"{uri.Scheme}://{NormalizedAuthority(uri)}{uri.AbsolutePath}"
            : "invalid-uri";

    private static bool MatchesAllowedAuthority(string allowed, Uri uri)
    {
        var authority = NormalizedAuthority(uri);
        if (StringComparer.OrdinalIgnoreCase.Equals(allowed, authority)) {
            return true;
        }

        return uri.IsDefaultPort && StringComparer.OrdinalIgnoreCase.Equals(allowed, uri.Host);
    }

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static bool SameUri(Uri left, Uri right)
        => StringComparer.OrdinalIgnoreCase.Equals(left.Scheme, right.Scheme) &&
           StringComparer.OrdinalIgnoreCase.Equals(NormalizedAuthority(left), NormalizedAuthority(right)) &&
           StringComparer.Ordinal.Equals(left.PathAndQuery, right.PathAndQuery);

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record SafeHttpRequest(CapabilityGrant Grant, Uri Uri, long MaxResponseBytes, TimeSpan Timeout);
}
