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

/// <summary>MCP tools for case value queries</summary>
[McpServerToolType]
[ToolRole(McpRole.HR)]
// ReSharper disable once UnusedType.Global
public sealed class CaseValueQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List all employee case values within a tenant</summary>
    [McpServerTool(Name = "list_employee_case_values"), Description(
        "List all case values of an employee (e.g. Salary, Address, BankAccount). " +
        "Returns the full case value history including start/end dates and current values.")]
    public async Task<string> ListEmployeeCaseValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier)
    {
        try
        {
            var (tenantContext, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            AssertEmployeeInDivision(employee);
            var employeeContext = new EmployeeServiceContext(tenantContext.TenantId, employee.Id);
            var values = await EmployeeCaseValueService().QueryAsync<CaseValue>(employeeContext);
            var result = new
            {
                employee = new
                {
                    employee.Identifier,
                    employee.FirstName,
                    employee.LastName
                },
                caseValues = values
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>List all company case values of a tenant</summary>
    [McpServerTool(Name = "list_company_case_values"), Description(
        "List all company case values of a tenant (e.g. company address, bank details, global settings). " +
        "Returns the full case value history including start/end dates and current values.")]
    public async Task<string> ListCompanyCaseValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var values = await CompanyCaseValueService().QueryAsync<CaseValue>(context);
            return JsonSerializer.Serialize(values);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
