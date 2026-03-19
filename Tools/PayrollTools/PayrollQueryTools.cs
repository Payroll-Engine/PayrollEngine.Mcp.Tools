using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.PayrollTools;

/// <summary>MCP tools for payroll, payrun and job queries</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class PayrollQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    #region Payrolls and Payruns

    /// <summary>List all payrolls of a tenant</summary>
    [McpServerTool(Name = "list_payrolls"), Description("List all payrolls of a tenant")]
    public async Task<string> ListPayrollsAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var payrolls = await PayrollService().QueryAsync<Payroll>(context, await IsolatedPayrollQueryAsync(tenantIdentifier));
            return JsonSerializer.Serialize(payrolls);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a payroll by name within a tenant</summary>
    [McpServerTool(Name = "get_payroll"), Description("Get a payroll by name within a tenant")]
    public async Task<string> GetPayrollAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var payroll = await PayrollService().GetAsync<Payroll>(context, payrollName);
            // Division isolation: guard — only payrolls of the configured division are accessible
            if (Isolation.Level == IsolationLevel.Division)
            {
                var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);
                if (!divisionId.HasValue || payroll?.DivisionId != divisionId.Value)
                {
                    throw new InvalidOperationException($"Access denied: payroll '{payrollName}' is not in division '{Isolation.DivisionName}'.");
                }
            }
            return JsonSerializer.Serialize(payroll);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>List all payruns of a tenant</summary>
    [McpServerTool(Name = "list_payruns"), Description("List all payruns of a tenant")]
    public async Task<string> ListPayrunsAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var payruns = await PayrunService().QueryAsync<Payrun>(context, await IsolatedPayrunQueryAsync(tenantIdentifier));
            return JsonSerializer.Serialize(payruns);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>List all payrun jobs of a tenant, ordered by creation date descending</summary>
    [McpServerTool(Name = "list_payrun_jobs"), Description(
        "List all payrun jobs of a tenant, ordered by creation date descending")]
    public async Task<string> ListPayrunJobsAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier)
    {
        try
        {
            var context = await ResolveTenantContextAsync(tenantIdentifier);
            var query = await IsolatedPayrunQueryAsync(tenantIdentifier);
            query.OrderBy = "created desc";
            var jobs = await PayrunJobService().QueryAsync<PayrunJob>(context, query);

            // resolve distinct division names
            var divisionNames = new Dictionary<int, string>();
            foreach (var divisionId in jobs.Select(j => j.DivisionId).Distinct())
            {
                var division = await DivisionService().GetAsync<Division>(context, divisionId);
                if (division != null)
                {
                    divisionNames[divisionId] = division.Name;
                }
            }

            var result = new
            {
                divisions = divisionNames,
                payrunJobs = jobs
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    #endregion

    #region Wage Types and Lookups

    /// <summary>List all effective wage types of a payroll merged across all regulation layers</summary>
    [McpServerTool(Name = "list_payroll_wage_types"), Description(
        "List all effective wage types of a payroll (merged across all regulation layers)")]
    public async Task<string> ListPayrollWageTypesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName)
    {
        try
        {
            // Division isolation: guard — only payrolls of the configured division are accessible
            if (Isolation.Level == IsolationLevel.Division)
            {
                var ctx = await ResolveTenantContextAsync(tenantIdentifier);
                var p = await PayrollService().GetAsync<Payroll>(ctx, payrollName);
                var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);
                if (!divisionId.HasValue || p?.DivisionId != divisionId.Value)
                {
                    throw new InvalidOperationException($"Access denied: payroll '{payrollName}' is not in division '{Isolation.DivisionName}'.");
                }
            }
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);
            var wageTypes = await PayrollService().GetWageTypesAsync<WageType>(context);
            return JsonSerializer.Serialize(wageTypes);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get a single lookup value from a payroll lookup by key</summary>
    [McpServerTool(Name = "get_payroll_lookup_value"), Description(
        "Get a single lookup value from a payroll lookup (merged across all regulation layers). " +
        "Use lookupKey for exact-key lookups (e.g. tax bracket identifier). " +
        "Use rangeValue for progressive/range lookups (e.g. income amount to find the matching bracket). " +
        "Returns the resolved value data including the JSON value field.")]
    public async Task<string> GetPayrollLookupValueAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("The lookup name")] string lookupName,
        [Description("The lookup key for exact-key lookups (omit for range-only lookups)")] string lookupKey = null,
        [Description("The numeric range value for progressive/range lookups (e.g. '85000' for income bracket lookup)")] string rangeValue = null,
        [Description("The culture name (e.g. 'de-CH') for localized lookup values (default: server culture)")] string culture = null)
    {
        try
        {
            // Division isolation: guard — only payrolls of the configured division are accessible
            if (Isolation.Level == IsolationLevel.Division)
            {
                var ctx = await ResolveTenantContextAsync(tenantIdentifier);
                var p = await PayrollService().GetAsync<Payroll>(ctx, payrollName);
                var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);
                if (!divisionId.HasValue || p?.DivisionId != divisionId.Value)
                {
                    throw new InvalidOperationException($"Access denied: payroll '{payrollName}' is not in division '{Isolation.DivisionName}'.");
                }
            }

            decimal? parsedRangeValue = null;
            if (!string.IsNullOrWhiteSpace(rangeValue))
            {
                if (!decimal.TryParse(rangeValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var rv))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Invalid rangeValue '{rangeValue}'. Must be a numeric value, e.g. '85000' or '85000.50'.",
                        type = nameof(FormatException)
                    });
                }
                parsedRangeValue = rv;
            }

            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);
            var valueData = await PayrollService().GetLookupValueDataAsync(
                context, lookupName, lookupKey, parsedRangeValue, culture: culture);
            return JsonSerializer.Serialize(valueData);
        }
        catch (Exception ex) { return Error(ex); }
    }

    #endregion

    #region Temporal Case Values

    /// <summary>Get case values at a specific point in time</summary>
    [McpServerTool(Name = "get_case_time_values"), Description(
        "Get case values (salary, address, bank account, etc.) valid at a specific point in time. " +
        "Supports three temporal perspectives: " +
        "(1) Historical — set valueDate and evaluationDate to the same date to see data 'as of that date', excluding later corrections. " +
        "(2) Current knowledge — set only valueDate; evaluationDate defaults to today, so retroactive corrections are visible. " +
        "(3) Forecast — set a forecast name and evaluationDate = valueDate to include planned future changes. " +
        "Omit employeeIdentifier to query all employees (Employee caseType only).")]
    public async Task<string> GetCaseTimeValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("Case type: Employee, Company, or Global (default: Employee)")] string caseType = "Employee",
        [Description("Comma-separated list of case field names, e.g. 'Salary,City,IBAN' (omit for all fields)")] string caseFieldNames = null,
        [Description("The value date (ISO 8601): which value is valid on this date, e.g. '2026-01-01' (default: today)")] string valueDate = null,
        [Description("The evaluation date (ISO 8601): knowledge perspective. Equal to valueDate = historical view; today = current knowledge (default: today)")] string evaluationDate = null,
        [Description("Employee identifier — required for a single employee; omit for all employees (Employee caseType only)")] string employeeIdentifier = null,
        [Description("Forecast name to include planned values (set evaluationDate = valueDate when using forecast)")] string forecast = null)
    {
        try
        {
            var payrollContext = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            if (!Enum.TryParse<CaseType>(caseType, ignoreCase: true, out var parsedCaseType))
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Unknown caseType '{caseType}'. Valid values: Employee, Company, Global.",
                    type = nameof(ArgumentException)
                });
            }

            // value date
            DateTime? parsedValueDate = null;
            if (!string.IsNullOrWhiteSpace(valueDate))
            {
                if (!DateTime.TryParse(valueDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var vd))
                {
                    return JsonSerializer.Serialize(new { error = $"Invalid valueDate '{valueDate}'. Use ISO 8601 format, e.g. '2026-01-01'.", type = nameof(FormatException) });
                }
                parsedValueDate = vd;
            }

            // evaluation date
            DateTime? parsedEvaluationDate = null;
            if (!string.IsNullOrWhiteSpace(evaluationDate))
            {
                if (!DateTime.TryParse(evaluationDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed))
                {
                    return JsonSerializer.Serialize(new { error = $"Invalid evaluationDate '{evaluationDate}'. Use ISO 8601 format, e.g. '2026-01-01'.", type = nameof(FormatException) });
                }
                parsedEvaluationDate = ed;
            }

            IEnumerable<string> fieldNames = null;
            if (!string.IsNullOrWhiteSpace(caseFieldNames))
            {
                fieldNames = caseFieldNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            // In Employee isolation, always scope to the configured employee — even if the AI omitted the identifier.
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

            var values = await PayrollService().GetCaseTimeValuesAsync(
                context: payrollContext,
                caseType: parsedCaseType,
                employeeId: employeeId,
                caseFieldNames: fieldNames,
                valueDate: parsedValueDate,
                evaluationDate: parsedEvaluationDate,
                forecast: string.IsNullOrWhiteSpace(forecast) ? null : forecast);

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
                    caseValues = values
                };
                return JsonSerializer.Serialize(result);
            }

            return JsonSerializer.Serialize(values);
        }
        catch (Exception ex) { return Error(ex); }
    }

    #endregion
}
