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

namespace PayrollEngine.Mcp.Tools.PayrollTools;

/// <summary>MCP tools for payroll result value queries</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class PayrollResultTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List payroll result values for a tenant</summary>
    [McpServerTool(Name = "list_payroll_result_values"), Description(
        "List payroll result values (wage types and collectors) for a tenant. " +
        "Each row is fully denormalized and includes employee identifier, payroll name, period name, " +
        "payrun name, job name, and result value — no separate lookups required. " +
        "Optionally filter by employee identifier or payroll name. " +
        "Use the OData filter for any field, e.g. \"resultKind eq 'WageType'\" or \"periodName eq 'January 2026'\". " +
        "Use top to limit results (default: 200).")]
    public async Task<string> ListPayrollResultValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("Employee identifier to filter results for a single employee (optional)")] string employeeIdentifier = null,
        [Description("Payroll name to filter results for a specific payroll (optional)")] string payrollName = null,
        [Description("OData filter expression, e.g. \"resultKind eq 'WageType'\" or \"periodName eq 'January 2026'\"")] string filter = null,
        [Description("Maximum number of results (default: 200)")] int top = 200)
    {
        try
        {
            var tenantContext = await ResolveTenantContextAsync(tenantIdentifier);

            // In Employee isolation, always scope to the configured employee — even if the AI omitted the identifier.
            if (string.IsNullOrWhiteSpace(employeeIdentifier) && Isolation.Level == IsolationLevel.Employee)
            {
                employeeIdentifier = Isolation.EmployeeIdentifier;
            }

            int? employeeId = null;
            if (!string.IsNullOrWhiteSpace(employeeIdentifier))
            {
                var (_, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
                employeeId = employee.Id;
            }

            int? payrollId = null;
            if (!string.IsNullOrWhiteSpace(payrollName))
            {
                var payrollContext = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);
                payrollId = payrollContext.PayrollId;
            }

            // In Division isolation, scope results to the configured division.
            var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);

            var valueContext = new PayrollResultValueServiceContext(
                tenantContext.TenantId,
                payrollId: payrollId,
                employeeId: employeeId,
                divisionId: divisionId);

            var query = new Query
            {
                Filter = filter,
                Top = top > 0 ? top : null
            };

            var values = await PayrollResultValueService().QueryAsync<PayrollResultValue>(valueContext, query);
            return JsonSerializer.Serialize(values);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
