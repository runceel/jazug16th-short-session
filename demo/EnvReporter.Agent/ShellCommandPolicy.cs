using System.Text.Json;
using GitHub.Copilot;

namespace EnvReporter.Agent;

/// <summary>
/// 組み込みシェルツールを読み取り専用コマンドに制限し、それ以外は実行前に拒否する。
/// </summary>
public sealed class ShellCommandPolicy(ILogger<ShellCommandPolicy> logger)
{
    // OS に応じて Copilot runtime のシェルツール名を選ぶ。
    public static string ShellToolName { get; } = OperatingSystem.IsWindows() ? "powershell" : "bash";

    public static IReadOnlyList<string> AllowedCommands { get; } = OperatingSystem.IsWindows()
        ?
        [
            "hostname",
            "[System.Runtime.InteropServices.RuntimeInformation]::OSDescription",
            "[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture",
            "[System.Environment]::ProcessorCount",
            "dotnet --info",
            "$env:FOUNDRY_HOSTING_ENVIRONMENT",
        ]
        :
        [
            "hostname",
            "uname -a",
            "cat /etc/os-release",
            "nproc",
            "free -h",
            "dotnet --info",
            "printenv FOUNDRY_HOSTING_ENVIRONMENT",
        ];

    public Task<PreToolUseHookOutput?> OnPreToolUseAsync(PreToolUseHookInput input, HookInvocation invocation)
    {
        if (!string.Equals(input.ToolName, ShellToolName, StringComparison.Ordinal))
        {
            return Deny(input.ToolName, command: null, $"Tool '{input.ToolName}' is not allowed. Use only '{ShellToolName}'.");
        }

        var command = GetCommand(input.ToolArgs);
        if (command is null || !AllowedCommands.Contains(command, StringComparer.Ordinal))
        {
            return Deny(input.ToolName, command,
                $"Command is not allowed. Run exactly one of: {string.Join(" | ", AllowedCommands)}");
        }

        logger.LogInformation("Allowed {Tool} command: {Command}", input.ToolName, command);
        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput { PermissionDecision = "allow" });
    }

    private Task<PreToolUseHookOutput?> Deny(string toolName, string? command, string reason)
    {
        logger.LogWarning("Denied {Tool} call. Command: {Command}", toolName, command ?? "(none)");
        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
        {
            PermissionDecision = "deny",
            PermissionDecisionReason = reason,
        });
    }

    private static string? GetCommand(JsonElement? toolArgs) =>
        toolArgs is { ValueKind: JsonValueKind.Object } args
        && args.TryGetProperty("command", out var command)
        && command.ValueKind == JsonValueKind.String
            ? command.GetString()?.Trim()
            : null;
}
