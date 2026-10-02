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
                // The Hosted Agent Responses protocol requires an API version; the OpenAI client omits it.
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
