using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools;

/// <summary>MCP tool for server metadata — always registered regardless of role or isolation level.</summary>
[McpServerToolType]
// ReSharper disable once UnusedType.Global
public sealed class ServerInfoTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>Get MCP server version, isolation level and active permissions</summary>
    [McpServerTool(Name = "get_server_info"), Description(
        "Get Payroll Engine MCP Server metadata: version, isolation level, configured scope, and active role permissions. " +
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
            isolationLevel = Isolation.Level.ToString(),
            scope,
            permissions = new
            {
                HR      = Isolation.Permissions.HR.ToString(),
                Payroll = Isolation.Permissions.Payroll.ToString(),
                Report  = Isolation.Permissions.Report.ToString(),
                System  = Isolation.Permissions.System.ToString()
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(info));
    }
}
