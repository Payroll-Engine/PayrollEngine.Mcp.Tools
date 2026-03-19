using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.PeopleTools;

/// <summary>MCP tools for user queries</summary>
[McpServerToolType]
[ToolRole(McpRole.System)]
// ReSharper disable once UnusedType.Global
public sealed class UserQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List all users of a tenant</summary>
    [McpServerTool(Name = "list_users"), Description("List all users of a tenant")]
    public async Task<string> ListUsersAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var users = await UserService().QueryAsync<User>(context, ActiveQuery());
            return JsonSerializer.Serialize(users);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a user by identifier within a tenant</summary>
    [McpServerTool(Name = "get_user"), Description("Get a user by identifier within a tenant")]
    public async Task<string> GetUserAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The user identifier (typically an email address)")] string userIdentifier)
    {
        try
        {
            var (_, user) = await ResolveUserAsync(tenantIdentifier, userIdentifier);
            return JsonSerializer.Serialize(user);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a single attribute value of a user</summary>
    [McpServerTool(Name = "get_user_attribute"), Description("Get a single attribute value of a user")]
    public async Task<string> GetUserAttributeAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The user identifier")] string userIdentifier,
        [Description("The attribute name")] string attributeName)
    {
        try
        {
            var (context, user) = await ResolveUserAsync(tenantIdentifier, userIdentifier);
            return await UserService().GetAttributeAsync(context, user.Id, attributeName);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
