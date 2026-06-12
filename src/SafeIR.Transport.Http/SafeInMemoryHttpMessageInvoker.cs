namespace SafeIR.Runtime;

using System.Net;
using System.Text;

public sealed class SafeInMemoryHttpMessageInvoker : HttpMessageInvoker
{
    public SafeInMemoryHttpMessageInvoker(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? location = null,
        string? finalRequestUri = null,
        TimeSpan? responseDelay = null)
        : this(Encoding.UTF8.GetBytes(response), statusCode, location, finalRequestUri, responseDelay)
    {
    }

    public SafeInMemoryHttpMessageInvoker(
        byte[] responseBytes,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? location = null,
        string? finalRequestUri = null,
        TimeSpan? responseDelay = null)
        : base(new InMemoryHandler(responseBytes, statusCode, Parse(location), Parse(finalRequestUri), responseDelay), disposeHandler: true)
    {
    }

    private static Uri? Parse(string? value) => value is null ? null : new Uri(value, UriKind.Absolute);

    private sealed class InMemoryHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly HttpStatusCode _statusCode;
        private readonly Uri? _location;
        private readonly Uri? _finalRequestUri;
        private readonly TimeSpan _responseDelay;

        public InMemoryHandler(
            byte[] responseBytes,
            HttpStatusCode statusCode,
            Uri? location,
            Uri? finalRequestUri,
            TimeSpan? responseDelay)
        {
            ArgumentNullException.ThrowIfNull(responseBytes);
            if (responseDelay is not null && responseDelay.Value < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(responseDelay));
            }

            _responseBytes = responseBytes.ToArray();
            _statusCode = statusCode;
            _location = location;
            _finalRequestUri = finalRequestUri;
            _responseDelay = responseDelay ?? TimeSpan.Zero;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responseDelay > TimeSpan.Zero) {
                await Task.Delay(_responseDelay, cancellationToken).ConfigureAwait(false);
            }

            var message = new HttpResponseMessage(_statusCode) {
                Content = new ByteArrayContent(_responseBytes),
                RequestMessage = _finalRequestUri is null
                    ? request
                    : new HttpRequestMessage(request.Method, _finalRequestUri)
            };
            if (_location is not null) {
                message.Headers.Location = _location;
            }

            return message;
        }
    }
}
