namespace EnvReporter.Web;

internal sealed class HostedAgentApiVersionHandler(bool enabled) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (enabled && request.RequestUri is { IsAbsoluteUri: true } requestUri)
        {
            var query = requestUri.Query.TrimStart('?');
            var hasApiVersion = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Any(parameter => parameter.Split('=', 2)[0]
                    .Equals("api-version", StringComparison.OrdinalIgnoreCase));

            if (!hasApiVersion)
            {
                // OpenAI client が省略する必須の API version を Hosted Agent endpoint に追加する。
                var uriBuilder = new UriBuilder(requestUri)
                {
                    Query = string.IsNullOrEmpty(query) ? "api-version=v1" : $"{query}&api-version=v1",
                };
                request.RequestUri = uriBuilder.Uri;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
