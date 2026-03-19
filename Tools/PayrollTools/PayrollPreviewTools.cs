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

/// <summary>MCP tools for payrun preview calculations</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class PayrollPreviewTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>Preview the payroll calculation for a single employee without persisting results</summary>
    [McpServerTool(Name = "get_employee_pay_preview"), Description(
        "Preview the payroll calculation for a single employee without persisting any results. " +
        "Returns wage type results, collector results, and payrun results as they would be calculated " +
        "for the given period. Use this to answer questions like 'What would the payroll look like for " +
        "Müller in April 2026?' or to verify regulation changes before running the actual payrun. " +
        "Requires McpServer:PreviewUserIdentifier to be configured (service account in the target tenant).")]
    public async Task<string> GetEmployeePayPreviewAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The employee identifier")] string employeeIdentifier,
        [Description("The payrun name")] string payrunName,
        [Description("Period start date (ISO 8601), e.g. '2026-04-01'")] string periodStart,
        [Description("Optional forecast name to include planned values")] string forecast = null,
        [Description("Optional reason for the preview run (default: 'Preview')")] string reason = null)
    {
        try
        {
            // validate PreviewUserIdentifier is configured
            if (string.IsNullOrWhiteSpace(Isolation.PreviewUserIdentifier))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "McpServer:PreviewUserIdentifier is not configured. " +
                            "Set a service account user identifier in appsettings.json to use this tool.",
                    type = nameof(InvalidOperationException)
                });
            }

            // parse period start
            if (!DateTime.TryParse(periodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedPeriodStart))
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Invalid periodStart '{periodStart}'. Use ISO 8601 format, e.g. '2026-04-01'.",
                    type = nameof(FormatException)
                });
            }

            // resolve tenant and employee
            var tenantContext = await ResolveTenantContextAsync(tenantIdentifier);
            var (_, employee) = await ResolveEmployeeAsync(tenantIdentifier, employeeIdentifier);
            AssertEmployeeInDivision(employee);

            var invocation = new PayrunJobInvocation
            {
                Name = $"mcp-preview-{employee.Identifier}",
                PayrunName = payrunName,
                UserIdentifier = Isolation.PreviewUserIdentifier,
                PeriodStart = parsedPeriodStart,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Preview" : reason,
                Forecast = string.IsNullOrWhiteSpace(forecast) ? null : forecast,
                EmployeeIdentifiers = [employee.Identifier]
            };

            var resultSet = await PayrunJobService().PreviewJobAsync<PayrollResultSet>(
                tenantContext, invocation);

            var result = new
            {
                employee = new
                {
                    employee.Identifier,
                    employee.FirstName,
                    employee.LastName
                },
                period = new { start = parsedPeriodStart },
                preview = resultSet
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }
}
