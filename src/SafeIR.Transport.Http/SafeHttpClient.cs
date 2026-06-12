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
        long? responseBytes = null;
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
            var metadataBytes = SafeHttpResponseAccounting.MeasureMetadataBytes(response);
            responseBytes = metadataBytes;
            SafeHttpResponseAccounting.ChargeMetadata(context, response, request.MaxResponseBytes);
            if (response.RequestMessage?.RequestUri is { } finalUri && !SafeHttpUriAudit.SameUri(finalUri, request.Uri))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

            if (IsRedirect(response.StatusCode))
            {
                throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: redirects are not allowed");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Error(SandboxErrorCode.HostFailure, "net.http.get failed: response status was not successful");
            }

            var body = await ReadLimitedTextAsync(
                    context,
                    response,
                    request.MaxResponseBytes - metadataBytes,
                    timeout.Token)
                .ConfigureAwait(false);
            responseBytes = metadataBytes + body.BytesRead;
            Audit(context, startedAt, true, resource, responseBytes, null);
            return body.Text;
        }
        catch (SandboxRuntimeException ex)
        {
            Audit(context, startedAt, false, resource, responseBytes, ex.Error.Code);
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
        var grantOptions = SafeHttpGrantReader.Read(grant);
        if (!Uri.TryCreate(sandboxUri.Value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: URI must be absolute");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: user info is not allowed");
        }

        RequireAllowedScheme(grantOptions, uri);
        RequireAllowedHost(grantOptions, uri);
        return new SafeHttpRequest(
            grantOptions,
            uri,
            grantOptions.MaxRequestBytes ?? context.Budget.Limits.MaxNetworkBytesWritten,
            grantOptions.MaxResponseBytes ?? context.Budget.Limits.MaxNetworkBytesRead,
            grantOptions.Timeout);
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
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > maxBytes)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var remaining = maxBytes - memory.Length;
            var readLimit = remaining >= buffer.Length ? buffer.Length : (int)remaining + 1;
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

            context.ChargeAllocation(read);
            memory.Write(buffer, 0, read);
        }

        var bodyLength = CheckedLength(memory.Length);
        var bytes = memory.GetBuffer();
        context.ChargeFuel(bodyLength);
        context.ChargeStringAllocation(Encoding.UTF8.GetCharCount(bytes, 0, bodyLength));
        var text = Encoding.UTF8.GetString(bytes, 0, bodyLength);
        context.RecordStringReturnCredit(text);
        return new LimitedText(text, bodyLength);
    }

    private static int CheckedLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw Error(SandboxErrorCode.QuotaExceeded, "net.http.get denied: response exceeds byte limit");
        }

        return (int)length;
    }

    private static void RequireAllowedScheme(SafeHttpGrantOptions grant, Uri uri)
    {
        if (!grant.AllowedSchemes.Contains(uri.Scheme))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: scheme is not allowed");
        }
    }

    private static void RequireAllowedHost(SafeHttpGrantOptions grant, Uri uri)
    {
        if (grant.AllowedHosts.Count == 0 || !grant.AllowedHosts.Any(host => SafeHttpUriAudit.MatchesAllowedAuthority(host, uri)))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: host is not allowed");
        }
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveVettedAddressesAsync(
        SafeHttpGrantOptions grant,
        string host,
        SafeDnsResolver dnsResolver,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            RequireIpLiteralAllowed(grant, address);
            return [address];
        }

        var addresses = await dnsResolver(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0 ||
            !grant.AllowPrivateNetwork && addresses.Any(SafeIpAddressClassifier.IsNonGlobal))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }

        return addresses;
    }

    private static void RequireIpLiteralAllowed(SafeHttpGrantOptions grant, IPAddress address)
    {
        if (!grant.AllowIpLiterals)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: IP literals are not allowed");
        }

        if (!grant.AllowPrivateNetwork && SafeIpAddressClassifier.IsNonGlobal(address))
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
            Bytes: bytes,
            Fields: context.BindingAuditFields("network", startedAt, bytesRead: bytes)));

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));

    private sealed record SafeHttpRequest(
        SafeHttpGrantOptions Grant,
        Uri Uri,
        long MaxRequestBytes,
        long MaxResponseBytes,
        TimeSpan Timeout);

    private sealed record LimitedText(string Text, long BytesRead);
}
