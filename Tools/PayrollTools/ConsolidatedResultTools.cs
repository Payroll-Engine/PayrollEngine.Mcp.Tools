using System;
using System.Globalization;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.PayrollTools;

/// <summary>MCP tools for consolidated payroll result queries</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class ConsolidatedResultTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>Get the consolidated payroll result for an employee and period</summary>
    [McpServerTool(Name = "get_consolidated_payroll_result"), Description(
        "Get the consolidated payroll result for a specific employee and period. " +
        "Returns all wage type results, collector results, and payrun results in a single response. " +
        "Use this for a complete overview of what was calculated for one employee in one period, " +
        "e.g. 'Show me all results for Müller in March 2026'. " +
        "periodStart and periodEnd define the period window (ISO 8601 dates).")]
    public async Task<string> GetConsolidatedPayrollResultAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier,
        [Description("Period start date (ISO 8601), e.g. '2026-03-01'")] string periodStart,
        [Description("Period end date (ISO 8601), e.g. '2026-03-31'")] string periodEnd,
        [Description("Optional forecast name to include planned values")] string forecast = null)
    {
        try
        {
            if (!DateTime.TryParse(periodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid periodStart '{periodStart}'. Use ISO 8601 format, e.g. '2026-03-01'.", type = nameof(FormatException) });
            }

            if (!DateTime.TryParse(periodEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid periodEnd '{periodEnd}'. Use ISO 8601 format, e.g. '2026-03-31'.", type = nameof(FormatException) });
            }

            var (tenantContext, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            AssertEmployeeInDivision(employee);

            // In Division isolation, scope results to the configured division.
            var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);

            var consolidated = await PayrollConsolidatedResultService().QueryPayrollResultAsync<ConsolidatedPayrollResult>(
                context: tenantContext,
                employeeId: employee.Id,
                periodStart: parsedStart,
                periodEnd: parsedEnd,
                divisionId: divisionId,
                forecast: string.IsNullOrWhiteSpace(forecast) ? null : forecast,
                jobStatus: null,
                tags: null);

            var result = new
            {
                employee = new
                {
                    employee.Identifier,
                    employee.FirstName,
                    employee.LastName
                },
                period = new
                {
                    start = parsedStart,
                    end = parsedEnd
                },
                consolidatedResult = consolidated
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
