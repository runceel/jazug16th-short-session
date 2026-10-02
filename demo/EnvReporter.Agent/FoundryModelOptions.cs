using System.ComponentModel.DataAnnotations;

namespace EnvReporter.Agent;

/// <summary>
/// Connection settings for the Foundry model deployment used through Copilot SDK BYOK.
/// </summary>
public sealed class FoundryModelOptions
{
    // Aspire injects these from the referenced model deployment resource named "chat".
    [ConfigurationKeyName("CHAT_URI")]
    [Required]
    public Uri? Endpoint { get; set; }

    [ConfigurationKeyName("CHAT_MODELNAME")]
    [Required]
    public string? DeploymentName { get; set; }
}
