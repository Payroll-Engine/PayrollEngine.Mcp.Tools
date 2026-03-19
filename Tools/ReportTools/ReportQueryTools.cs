using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.ReportTools;

/// <summary>MCP tools for payroll report execution</summary>
[McpServerToolType]
[ToolRole(McpRole.Report)]
// ReSharper disable once UnusedType.Global
public sealed class ReportQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>Execute a payroll report and return the result data set</summary>
    [McpServerTool(Name = "execute_payroll_report"), Description(
        "Execute a payroll report and return its result data set. " +
        "The report is resolved across all regulation layers of the payroll. " +
        "Use the parameters dictionary to pass report-specific input values (e.g. period, employee filter). " +
        "The result contains one or more named tables that the AI can analyse and summarise. " +
        "Requires McpServer:PreviewUserIdentifier to be configured (service account in the target tenant).")]
    public async Task<string> ExecutePayrollReportAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("The report name")] string reportName,
        [Description("Optional report parameters as key-value pairs, e.g. {\"Period\": \"2026-01\", \"EmployeeId\": \"123\"}")] Dictionary<string, string> parameters = null,
        [Description("The culture name for localized report output (e.g. 'de-CH')")] string culture = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Isolation.PreviewUserIdentifier))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "McpServer:PreviewUserIdentifier is not configured. " +
                            "Set a service account user identifier in appsettings.json to use this tool.",
                    type = nameof(InvalidOperationException)
                });
            }

            // resolve payroll → report (GetDerivedReports across all layers)
            var payrollContext = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);
            var (regulationContext, report) = await ResolveReportAsync(payrollContext, reportName);

            // resolve UserIdentifier → UserId (backend only accepts UserId)
            var (_, user) = await ResolveUserAsync(tenantIdentifier, Isolation.PreviewUserIdentifier);

            var request = new ReportRequest
            {
                UserId = user.Id,
                UserIdentifier = Isolation.PreviewUserIdentifier,
                Culture = culture,
                Parameters = parameters
            };

            var response = await ReportService().ExecuteReportAsync(regulationContext, report.Id, request);

            return JsonSerializer.Serialize(new
            {
                reportName = response.ReportName,
                culture = response.Culture,
                parameters = response.Parameters,
                result = response.Result
            });
        }
        catch (Exception ex) { return Error(ex); }
    }
}
