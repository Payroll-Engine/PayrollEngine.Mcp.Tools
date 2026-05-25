using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.PayrollTools;

/// <summary>MCP tools for case schema discovery — available cases and case build definitions</summary>
[McpServerToolType]
[ToolRole(McpRole.HR)]
// ReSharper disable once UnusedType.Global
public sealed class CaseSchemaQueryTools(PayrollHttpClient httpClient, IsolationContext isolation)
    : ToolBase(httpClient, isolation)
{
    /// <summary>List all cases available for data entry within a payroll</summary>
    [McpServerTool(Name = "get_available_cases"), Description(
        "List all cases available for data entry within a payroll (e.g. Employment, Salary, Address, BankAccount). " +
        "Availability is script-evaluated per user context, so only cases the user can actually interact with are returned. " +
        "Use this before build_case to discover which cases exist for a given case type. " +
        "Supports Employee, Company, and Global case types.")]
    public async Task<string> GetAvailableCasesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("The user identifier used to evaluate case availability scripts (falls back to the server PreviewUserIdentifier when omitted)")] string userIdentifier = null,
        [Description("Case type: Employee, Company, or Global (default: Employee)")] string caseType = "Employee",
        [Description("The employee identifier — required when caseType is Employee")] string employeeIdentifier = null)
    {
        try
        {
            if (!Enum.TryParse<CaseType>(caseType, ignoreCase: true, out var parsedCaseType))
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Unknown caseType '{caseType}'. Valid values: Employee, Company, Global.",
                    type = nameof(ArgumentException)
                });
            }

            var effectiveUserIdentifier = ResolveEffectiveUserIdentifier(userIdentifier);
            var payrollContext = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);
            var (_, user) = await ResolveUserAsync(tenantIdentifier, effectiveUserIdentifier);

            // In Employee isolation, always scope to the configured employee
            if (string.IsNullOrWhiteSpace(employeeIdentifier) && Isolation.Level == IsolationLevel.Employee)
            {
                employeeIdentifier = Isolation.EmployeeIdentifier;
            }

            int? employeeId = null;
            Employee resolvedEmployee = null;
            if (!string.IsNullOrWhiteSpace(employeeIdentifier))
            {
                var (_, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
                AssertEmployeeInDivision(employee);
                employeeId = employee.Id;
                resolvedEmployee = employee;
            }

            var cases = await PayrollService().GetAvailableCasesAsync<Case>(
                context: payrollContext,
                userId: user.Id,
                caseType: parsedCaseType,
                employeeId: employeeId);

            if (resolvedEmployee != null)
            {
                var result = new
                {
                    employee = new
                    {
                        resolvedEmployee.Identifier,
                        resolvedEmployee.FirstName,
                        resolvedEmployee.LastName
                    },
                    cases
                };
                return JsonSerializer.Serialize(result);
            }

            return JsonSerializer.Serialize(cases);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Resolves the effective user identifier: explicit parameter, then PreviewUserIdentifier fallback.</summary>
    private string ResolveEffectiveUserIdentifier(string userIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(userIdentifier))
        {
            return userIdentifier;
        }
        if (!string.IsNullOrWhiteSpace(Isolation.PreviewUserIdentifier))
        {
            return Isolation.PreviewUserIdentifier;
        }
        throw new InvalidOperationException(
            "userIdentifier is required. Provide it as a parameter or configure McpServer:PreviewUserIdentifier on the server.");
    }
}
