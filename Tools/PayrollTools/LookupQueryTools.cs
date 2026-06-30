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

/// <summary>MCP tools for lookup discovery, value retrieval, and historical comparison across regulation cycles</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class LookupQueryTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List all lookups of a payroll with metadata (description, attributes, range mode)</summary>
    [McpServerTool(Name = "query_lookups"), Description(
        "List all lookups of a payroll merged across regulation layers. " +
        "Returns lookup names, descriptions, attributes, and range configuration. " +
        "Use attributePrefix to filter lookups by attribute namespace, e.g. 'compliance' " +
        "to find lookups with compliance.* attributes. " +
        "Use regulationDate to query lookups as they were at a specific point in time.")]
    public async Task<string> QueryLookupsAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("Optional attribute prefix to filter lookups, e.g. 'compliance' matches lookups with any 'compliance.*' attribute")] string attributePrefix = null,
        [Description("Optional regulation date (ISO 8601) for historical queries, e.g. '2024-01-01'")] string regulationDate = null)
    {
        try
        {
            await AssertPayrollDivisionAsync(tenantIdentifier, payrollName);
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            DateTime? parsedRegulationDate = null;
            if (!string.IsNullOrWhiteSpace(regulationDate))
            {
                if (!DateTime.TryParse(regulationDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var rd))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Invalid regulationDate '{regulationDate}'. Use ISO 8601 format, e.g. '2024-01-01'.",
                        type = nameof(FormatException)
                    });
                }
                parsedRegulationDate = rd;
            }

            var lookups = await PayrollService().GetLookupsAsync<Lookup>(
                context, regulationDate: parsedRegulationDate);

            if (!string.IsNullOrWhiteSpace(attributePrefix))
            {
                var prefix = attributePrefix.EndsWith('.') ? attributePrefix : attributePrefix + ".";
                lookups = lookups
                    .Where(l => l.Attributes != null &&
                                l.Attributes.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var result = lookups.Select(l => new
            {
                l.Name,
                l.Description,
                l.RangeMode,
                l.RangeSize,
                l.Attributes
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Get all values of a specific lookup</summary>
    [McpServerTool(Name = "get_lookup_values"), Description(
        "Get all values of a specific lookup within a payroll. " +
        "Returns the lookup metadata (description, attributes) together with all key-value pairs. " +
        "Use regulationDate to query values as they were at a specific point in time.")]
    public async Task<string> GetLookupValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("The lookup name")] string lookupName,
        [Description("Optional regulation date (ISO 8601) for historical queries, e.g. '2024-01-01'")] string regulationDate = null,
        [Description("Optional culture for localized values, e.g. 'de-DE'")] string culture = null)
    {
        try
        {
            await AssertPayrollDivisionAsync(tenantIdentifier, payrollName);
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            DateTime? parsedRegulationDate = null;
            if (!string.IsNullOrWhiteSpace(regulationDate))
            {
                if (!DateTime.TryParse(regulationDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var rd))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Invalid regulationDate '{regulationDate}'. Use ISO 8601 format, e.g. '2024-01-01'.",
                        type = nameof(FormatException)
                    });
                }
                parsedRegulationDate = rd;
            }

            var lookups = await PayrollService().GetLookupsAsync<Lookup>(
                context, lookupNames: [lookupName], regulationDate: parsedRegulationDate);
            var lookup = lookups?.FirstOrDefault();
            if (lookup == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Lookup '{lookupName}' not found in payroll '{payrollName}'.",
                    type = nameof(InvalidOperationException)
                });
            }

            var lookupData = await PayrollService().GetLookupDataAsync<LookupData>(
                context, [lookupName], regulationDate: parsedRegulationDate, culture: culture);
            var data = lookupData?.FirstOrDefault();

            var result = new
            {
                lookup.Name,
                lookup.Description,
                lookup.RangeMode,
                lookup.RangeSize,
                lookup.Attributes,
                Values = data?.Values?.Select(v => new
                {
                    v.Key,
                    v.Value,
                    v.RangeValue
                })
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Compare a lookup across all regulation cycles (validFrom dates)</summary>
    [McpServerTool(Name = "compare_lookup_values"), Description(
        "Compare a lookup's values across all regulation cycles. " +
        "Retrieves all payroll regulations, extracts their validFrom dates, " +
        "and queries the lookup at each cycle. " +
        "Use this for historical compliance analysis, e.g. 'How did social security ceilings evolve from 2024 to 2026?'")]
    public async Task<string> CompareLookupValuesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("The lookup name to compare across cycles")] string lookupName,
        [Description("Optional culture for localized values, e.g. 'de-DE'")] string culture = null)
    {
        try
        {
            await AssertPayrollDivisionAsync(tenantIdentifier, payrollName);
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            // step 1: get all payroll regulations to extract validFrom dates
            var regulations = await PayrollService().GetRegulationsAsync<Regulation>(context);
            var cycles = regulations
                .Where(r => r.ValidFrom.HasValue)
                .Select(r => r.ValidFrom!.Value)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (cycles.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "No regulations with validFrom dates found.",
                    type = nameof(InvalidOperationException)
                });
            }

            // step 2: query lookup values at each cycle
            var cycleResults = new List<object>();
            foreach (var cycle in cycles)
            {
                var lookupData = await PayrollService().GetLookupDataAsync<LookupData>(
                    context, [lookupName], regulationDate: cycle, culture: culture);
                var data = lookupData?.FirstOrDefault();

                cycleResults.Add(new
                {
                    regulationDate = cycle.ToString("yyyy-MM-dd"),
                    values = data?.Values?.Select(v => new
                    {
                        v.Key,
                        v.Value,
                        v.RangeValue
                    })
                });
            }

            // step 3: include lookup metadata from the latest cycle
            var latestLookups = await PayrollService().GetLookupsAsync<Lookup>(
                context, lookupNames: [lookupName]);
            var latestLookup = latestLookups?.FirstOrDefault();

            var result = new
            {
                lookup = latestLookup == null ? null : new
                {
                    latestLookup.Name,
                    latestLookup.Description,
                    latestLookup.RangeMode,
                    latestLookup.Attributes
                },
                cycles = cycleResults
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>Division isolation guard for payroll access</summary>
    private async System.Threading.Tasks.Task AssertPayrollDivisionAsync(string tenantIdentifier, string payrollName)
    {
        if (Isolation.Level != IsolationLevel.Division)
        {
            return;
        }
        var ctx = await ResolveTenantContextAsync(tenantIdentifier);
        var payroll = await PayrollService().GetAsync<Payroll>(ctx, payrollName);
        var divisionId = await ResolveIsolatedDivisionIdAsync(tenantIdentifier);
        if (!divisionId.HasValue || payroll?.DivisionId != divisionId.Value)
        {
            throw new InvalidOperationException(
                $"Access denied: payroll '{payrollName}' is not in division '{Isolation.DivisionName}'.");
        }
    }
}
