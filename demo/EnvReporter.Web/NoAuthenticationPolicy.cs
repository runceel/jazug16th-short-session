using System.ClientModel.Primitives;

namespace EnvReporter.Web;

internal sealed class NoAuthenticationPolicy : AuthenticationPolicy
{
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex) =>
        ProcessNext(message, pipeline, currentIndex);

    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex) =>
        ProcessNextAsync(message, pipeline, currentIndex);
}
