namespace SafeIR.Runtime;

using System.Net;
using System.Text;
using SafeIR;

public delegate ValueTask<IReadOnlyList<IPAddress>> SafeDnsResolver(string host, CancellationToken cancellationToken);

public static class SafeHttpClient
{
    public static async ValueTask<string> GetTextAsync(
        SandboxContext context,
        SandboxUri uri,
        SafeInMemoryHttpMessageInvoker? invoker,
        SafeDnsResolver? dnsResolver,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var resource = SafeHttpUriAudit.Sanitize(uri.Value);
        try
        {
            var request = ResolveRequest(context, uri);
            ChargeRequestBytes(context, request);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(EffectiveTimeout(context, request.Timeout));
            var addresses = await ResolveVettedAddressesAsync(
                    request.Grant,
                    request.Uri.Host,
                    dnsResolver ?? ResolveDnsAsync,
                    timeout.Token)
                .ConfigureAwait(false);
            using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
            using var pinnedResponse = await SafePinnedHttpTransport.SendAsync(invoker, message, addresses, timeout.Token)
                .ConfigureAwait(false);
            var response = pinnedResponse.Message;
            if (response.RequestMessage?.RequestUri is { } finalUri && !SafeHttpUriAudit.SameUri(finalUri, request.Uri))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

            if (IsRedirect(response.StatusCode))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

            response.EnsureSuccessStatusCode();
            var body = await ReadLimitedTextAsync(context, response, request.MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            Audit(context, startedAt, true, resource, body.BytesRead, null);
            return body.Text;
        }
        catch (SandboxRuntimeException ex)
        {
            Audit(context, startedAt, false, resource, null, ex.Error.Code);
            throw;
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            var error = new SandboxError(SandboxErrorCode.Timeout, "net.http.get denied: request timed out");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "net.http.get cancelled");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
        catch (Exception)
        {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "net.http.get failed");
            Audit(context, startedAt, false, resource, null, error.Code);
            throw new SandboxRuntimeException(error);
        }
    }

    private static SafeHttpRequest ResolveRequest(SandboxContext context, SandboxUri sandboxUri)
    {
        context.RequireCapability("net.http.get");
        var grant = context.GetCapability("net.http.get");
        if (!Uri.TryCreate(sandboxUri.Value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: URI must be absolute");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: user info is not allowed");
        }

        RequireAllowedScheme(grant, uri);
        RequireAllowedHost(grant, uri);
        return new SafeHttpRequest(
            grant,
            uri,
            SafeHttpGrantReader.ReadLong(grant, "maxRequestBytes", context.Budget.Limits.MaxNetworkBytesWritten),
            SafeHttpGrantReader.ReadLong(grant, "maxResponseBytes", context.Budget.Limits.MaxNetworkBytesRead),
            SafeHttpGrantReader.ReadTimeout(grant));
    }

    private static void ChargeRequestBytes(SandboxContext context, SafeHttpRequest request)
    {
        var bytes = Encoding.UTF8.GetByteCount("GET " + request.Uri.AbsoluteUri);
        if (bytes > request.MaxRequestBytes)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: request exceeds byte limit");
        }

        context.Budget.ChargeNetworkWrite(bytes);
    }

    private static async ValueTask<LimitedText> ReadLimitedTextAsync(
        SandboxContext context,
        HttpResponseMessage response,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var remaining = maxBytes - memory.Length;
            var readLimit = (int)Math.Min(buffer.Length, remaining + 1);
            var read = await stream.ReadAsync(buffer.AsMemory(0, readLimit), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            context.Budget.ChargeNetworkRead(read);
            if (read > remaining)
            {
                throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
            }

            memory.Write(buffer, 0, read);
        }

        var bytes = memory.ToArray();
        context.ChargeFuel(bytes.Length);
        context.ChargeAllocation(bytes.Length);
        var text = Encoding.UTF8.GetString(bytes);
        context.ChargeString(text);
        return new LimitedText(text, bytes.Length);
    }

    private static void RequireAllowedScheme(CapabilityGrant grant, Uri uri)
    {
        var allowed = SafeHttpGrantReader.ReadSet(grant, "allowedSchemes", ["https"]);
        if (!allowed.Contains(uri.Scheme))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: scheme is not allowed");
        }
    }

    private static void RequireAllowedHost(CapabilityGrant grant, Uri uri)
    {
        var allowed = SafeHttpGrantReader.ReadSet(grant, "allowedHosts", []);
        if (allowed.Count == 0 || !allowed.Any(host => SafeHttpUriAudit.MatchesAllowedAuthority(host, uri)))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: host is not allowed");
        }
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveVettedAddressesAsync(
        CapabilityGrant grant,
        string host,
        SafeDnsResolver dnsResolver,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            RequireIpLiteralAllowed(grant, address);
            return [address];
        }

        var allowPrivateNetwork = SafeHttpGrantReader.ReadBool(grant, "allowPrivateNetwork");
        var addresses = await dnsResolver(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0 ||
            !allowPrivateNetwork && addresses.Any(SafeIpAddressClassifier.IsNonGlobal))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }

        return addresses;
    }

    private static void RequireIpLiteralAllowed(CapabilityGrant grant, IPAddress address)
    {
        if (!SafeHttpGrantReader.ReadBool(grant, "allowIpLiterals"))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: IP literals are not allowed");
        }

        if (!SafeHttpGrantReader.ReadBool(grant, "allowPrivateNetwork") && SafeIpAddressClassifier.IsNonGlobal(address))
        {
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

    private static TimeSpan EffectiveTimeout(SandboxContext context, TimeSpan requestTimeout)
    {
        var remaining = context.Budget.RemainingWallTime();
        return remaining < requestTimeout ? remaining : requestTimeout;
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveDnsAsync(
        string host,
        CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

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

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record SafeHttpRequest(CapabilityGrant Grant, Uri Uri, long MaxRequestBytes, long MaxResponseBytes, TimeSpan Timeout);

    private sealed record LimitedText(string Text, long BytesRead);
}
