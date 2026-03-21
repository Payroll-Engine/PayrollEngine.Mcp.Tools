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

/// <summary>MCP tools for employee queries</summary>
[McpServerToolType]
[ToolRole(McpRole.HR)]
// ReSharper disable once UnusedType.Global
public sealed class EmployeeQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List employees of a tenant</summary>
    [McpServerTool(Name = "list_employees"), Description(
        "List employees of a tenant. " +
        "Use the filter parameter to narrow results by any employee field, e.g. \"lastName eq 'Müller'\" or \"contains(identifier, 'acme')\". " +
        "Use top to limit the result count (default: 200).")]
    public async Task<string> ListEmployeesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("OData filter expression, e.g. \"lastName eq 'Müller'\" or \"contains(identifier, 'acme')\"")] string filter = null,
        [Description("Maximum number of results (default: 200)")] int top = 200)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var query = IsolatedEmployeeQuery(filter, top > 0 ? top : null);
            var employees = await EmployeeService().QueryAsync<Employee>(context, query);
            return JsonSerializer.Serialize(employees);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get an employee by identifier within a tenant</summary>
    [McpServerTool(Name = "get_employee"), Description("Get an employee by identifier within a tenant")]
    public async Task<string> GetEmployeeAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier)
    {
        try
        {
            var (_, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            return JsonSerializer.Serialize(employee);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a single attribute value of an employee</summary>
    [McpServerTool(Name = "get_employee_attribute"), Description("Get a single attribute value of an employee")]
    public async Task<string> GetEmployeeAttributeAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier,
        [Description("The attribute name")] string attributeName)
    {
        try
        {
            var (context, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            return await EmployeeService().GetAttributeAsync(context, employee.Id, attributeName);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
