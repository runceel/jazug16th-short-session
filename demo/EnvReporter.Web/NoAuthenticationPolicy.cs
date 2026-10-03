using System.ClientModel.Primitives;

namespace EnvReporter.Web;

// ローカル Aspire の内部 endpoint 向けに、認証処理を行わず pipeline を続行する。
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
