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

namespace PayrollEngine.Mcp.Tools.PeopleTools;

/// <summary>MCP tools for case change queries</summary>
[McpServerToolType]
[ToolRole(McpRole.HR)]
// ReSharper disable once UnusedType.Global
public sealed class CaseChangeQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List all case changes of an employee within a tenant</summary>
    [McpServerTool(Name = "list_employee_case_changes"), Description(
        "List all case changes (audit trail of data mutations) of an employee. " +
        "Each change contains the user who made it, the payroll, the reason, and all affected case values with old and new values. " +
        "Use this to answer 'who changed what and when' for a specific employee. " +
        "Use the OData filter to narrow results, e.g. \"created gt 2026-01-01\" or \"reason eq 'Salary adjustment'\". " +
        "Use top to limit results (default: 100).")]
    public async Task<string> ListEmployeeCaseChangesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier,
        [Description("OData filter expression, e.g. \"created gt 2026-01-01\" or \"reason eq 'Salary adjustment'\"")] string filter = null,
        [Description("Maximum number of results (default: 100)")] int top = 100)
    {
        try
        {
            var (tenantContext, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            AssertEmployeeInDivision(employee);
            var employeeContext = new EmployeeServiceContext(tenantContext.TenantId, employee.Id);

            var query = new CaseChangeQuery
            {
                Filter = filter,
                Top = top > 0 ? top : null
            };

            var changes = await EmployeeCaseChangeService().QueryAsync<CaseChange>(employeeContext, query);
            var result = new
            {
                employee = new
                {
                    employee.Identifier,
                    employee.FirstName,
                    employee.LastName
                },
                caseChanges = changes
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>List all company case changes within a tenant</summary>
    [McpServerTool(Name = "list_company_case_changes"), Description(
        "List all company-level case changes (audit trail of company data mutations) within a tenant. " +
        "Each change contains the user who made it, the payroll, the reason, and all affected case values. " +
        "Use this to answer 'who changed which company setting and when'. " +
        "Use the OData filter to narrow results, e.g. \"created gt 2026-01-01\". " +
        "Use top to limit results (default: 100).")]
    public async Task<string> ListCompanyCaseChangesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("OData filter expression, e.g. \"created gt 2026-01-01\"")] string filter = null,
        [Description("Maximum number of results (default: 100)")] int top = 100)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);

            var query = new CaseChangeQuery
            {
                Filter = filter,
                Top = top > 0 ? top : null
            };

            var changes = await CompanyCaseChangeService().QueryAsync<CaseChange>(context, query);
            return JsonSerializer.Serialize(changes);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
