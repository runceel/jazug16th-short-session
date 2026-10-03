using System.ComponentModel.DataAnnotations;

namespace EnvReporter.Agent;

/// <summary>
/// Copilot SDK の BYOK で使う Foundry model deployment の接続設定。
/// </summary>
public sealed class FoundryModelOptions
{
    // Aspire が model deployment リソース chat から注入する。
    [ConfigurationKeyName("CHAT_URI")]
    [Required]
    public Uri? Endpoint { get; set; }

    [ConfigurationKeyName("CHAT_MODELNAME")]
    [Required]
    public string? DeploymentName { get; set; }
}
