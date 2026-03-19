using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Client.Service;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.TenantTools;

/// <summary>MCP tools for tenant queries</summary>
[McpServerToolType]
[ToolRole(McpRole.System)]
// ReSharper disable once UnusedType.Global
public sealed class TenantQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    private static readonly RootServiceContext Context = new();

    /// <summary>List all tenants</summary>
    [McpServerTool(Name = "list_tenants"), Description("List all tenants in the Payroll Engine")]
    public async Task<string> ListTenantsAsync()
    {
        try
        {
            var tenants = await TenantService().QueryAsync<Tenant>(Context, IsolatedTenantQuery());
            return JsonSerializer.Serialize(tenants);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a tenant by identifier</summary>
    [McpServerTool(Name = "get_tenant"), Description("Get a tenant by its identifier")]
    public async Task<string> GetTenantAsync(
        [Description("The unique tenant identifier")] string identifier)
    {
        try
        {
            // Route through ResolveTenantAsync to enforce isolation (EffectiveTenant override).
            // Direct TenantService.GetAsync bypasses isolation and must not be used here.
            var tenant = await ResolveTenantAsync(identifier);
            return JsonSerializer.Serialize(tenant);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a single tenant attribute by name</summary>
    [McpServerTool(Name = "get_tenant_attribute"), Description(
        "Get a single attribute value of a tenant by attribute name")]
    public async Task<string> GetTenantAttributeAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The attribute name")] string attributeName)
    {
        try
        {
            var tenant = await ResolveTenantAsync(tenantIdentifier);
            return await TenantService().GetAttributeAsync(Context, tenant.Id, attributeName);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
