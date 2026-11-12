using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools;

/// <summary>MCP tool for server metadata — always registered regardless of role or isolation level.
/// Needs no backend: the backend client is optional and only reported as connected or offline.</summary>
[McpServerToolType]
// ReSharper disable once UnusedType.Global
public sealed class ServerInfoTools(IsolationContext isolation, PayrollHttpClient httpClient = null)
    : IsolationToolBase(isolation)
{
    /// <summary>Get MCP server version, Lex version, isolation level, regulator mode, backend mode and active permissions</summary>
    [McpServerTool(Name = "get_server_info"), Description(
        "Get Payroll Engine MCP Server metadata: version, Lex library version, isolation level, configured scope, " +
        "active role permissions, regulator mode (permission, workspace roots, backend access) and backend mode (connected or offline). " +
        "Use this to verify which server build is running and how it is configured.")]
    public Task<string> GetServerInfoAsync()
    {
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version
            ?? "unknown";

        var scope = Isolation.Level switch
        {
            IsolationLevel.MultiTenant => "all tenants",
            IsolationLevel.Tenant      => $"tenant: {Isolation.TenantIdentifier}",
            IsolationLevel.Division    => $"tenant: {Isolation.TenantIdentifier}, division: {Isolation.DivisionName}",
            IsolationLevel.Employee    => $"tenant: {Isolation.TenantIdentifier}, employee: {Isolation.EmployeeIdentifier}",
            _                          => "unknown"
        };

        var info = new
        {
            product = "Payroll Engine MCP Server",
            version,
            lexVersion = GetLexVersion(),
            isolationLevel = Isolation.Level.ToString(),
            scope,
            permissions = new
            {
                HR        = Isolation.Permissions.HR.ToString(),
                Payroll   = Isolation.Permissions.Payroll.ToString(),
                Report    = Isolation.Permissions.Report.ToString(),
                System    = Isolation.Permissions.System.ToString(),
                Regulator = Isolation.Permissions.Regulator.ToString()
            },
            regulator = new
            {
                mode = Isolation.Permissions.Regulator.ToString(),
                rootCount = Workspace.Roots.Count,
                repoMask = Workspace.RepoMask,
                backend = Workspace.Backend.ToString()
            },
            backend = httpClient != null ? "connected" : "offline"
        };

        return Task.FromResult(JsonSerializer.Serialize(info));
    }

    /// <summary>The version of the Lex library the regulator tools run on</summary>
    private static string GetLexVersion()
    {
        var assembly = typeof(Checker).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unknown";

        // strip the source revision suffix (+hash)
        var separator = version.IndexOf('+');
        return separator > 0 ? version[..separator] : version;
    }
}
