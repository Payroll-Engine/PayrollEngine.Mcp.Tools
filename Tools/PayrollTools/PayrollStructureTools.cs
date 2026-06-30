using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using PayrollEngine.Client;
using PayrollEngine.Client.Model;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.PayrollTools;

/// <summary>MCP tools for payroll regulation structure queries — cases, collectors, and their relationships</summary>
[McpServerToolType]
[ToolRole(McpRole.Payroll)]
// ReSharper disable once UnusedType.Global
public sealed class PayrollStructureTools(PayrollHttpClient httpClient, IsolationContext isolation) : ToolBase(httpClient, isolation)
{
    /// <summary>List all cases of a payroll merged across regulation layers</summary>
    [McpServerTool(Name = "list_payroll_cases"), Description(
        "List all cases of a payroll merged across all regulation layers. " +
        "Returns case names, types, descriptions, clusters, and attributes. " +
        "Use caseType to filter by Employee, Company, or Global cases. " +
        "Use cluster to filter by cluster membership, e.g. 'GC.Tax' for Global Case tax mappings " +
        "or 'GC.All' for all Global Cases.")]
    public async Task<string> ListPayrollCasesAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("Optional case type filter: Employee, Company, or Global")] string caseType = null,
        [Description("Optional cluster name to filter cases, e.g. 'GC.Tax' or 'GC.All'")] string cluster = null)
    {
        try
        {
            await AssertPayrollDivisionAsync(tenantIdentifier, payrollName);
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            CaseType? parsedCaseType = null;
            if (!string.IsNullOrWhiteSpace(caseType))
            {
                if (!Enum.TryParse<CaseType>(caseType, ignoreCase: true, out var ct))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Unknown caseType '{caseType}'. Valid values: Employee, Company, Global.",
                        type = nameof(ArgumentException)
                    });
                }
                parsedCaseType = ct;
            }

            var cases = await PayrollService().GetCasesAsync<Case>(context, caseType: parsedCaseType);

            if (!string.IsNullOrWhiteSpace(cluster))
            {
                cases = cases
                    .Where(c => c.Clusters != null &&
                                c.Clusters.Contains(cluster, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            var result = cases.Select(c => new
            {
                c.Name,
                c.CaseType,
                c.Description,
                c.Clusters,
                c.BaseCase,
                c.Hidden,
                c.CancellationType,
                c.Lookups,
                c.Attributes
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) { return Error(ex); }
    }

    /// <summary>List all collectors of a payroll with their contributing wage types</summary>
    [McpServerTool(Name = "list_payroll_collectors"), Description(
        "List all collectors of a payroll merged across regulation layers, " +
        "together with the wage types that feed each collector. " +
        "A wage type contributes to a collector either directly (via its Collectors list) " +
        "or indirectly (via CollectorGroups that the collector belongs to). " +
        "Use this to understand payroll aggregation structure, e.g. 'What feeds GrossIncome?' " +
        "or 'Which wage types contribute to TotalDeductions?'")]
    public async Task<string> ListPayrollCollectorsAsync(
        [Description("The unique tenant identifier")] string tenantIdentifier,
        [Description("The payroll name")] string payrollName,
        [Description("Optional collector name to get details for a single collector")] string collectorName = null,
        [Description("Optional cluster name to filter collectors")] string cluster = null)
    {
        try
        {
            await AssertPayrollDivisionAsync(tenantIdentifier, payrollName);
            var context = await ResolvePayrollContextAsync(tenantIdentifier, payrollName);

            IEnumerable<string> collectorNames = null;
            if (!string.IsNullOrWhiteSpace(collectorName))
            {
                collectorNames = [collectorName];
            }

            var collectors = await PayrollService().GetCollectorsAsync<Collector>(context, collectorNames: collectorNames);

            if (!string.IsNullOrWhiteSpace(cluster))
            {
                collectors = collectors
                    .Where(c => c.Clusters != null &&
                                c.Clusters.Contains(cluster, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            var wageTypes = await PayrollService().GetWageTypesAsync<WageType>(context);

            // build collector → groups lookup
            var collectorGroupMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var collector in collectors)
            {
                if (collector.CollectorGroups == null)
                {
                    continue;
                }
                foreach (var group in collector.CollectorGroups)
                {
                    if (!collectorGroupMap.ContainsKey(group))
                    {
                        collectorGroupMap[group] = [];
                    }
                    collectorGroupMap[group].Add(collector.Name);
                }
            }

            // map wage types to collectors
            var collectorContributors = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var collector in collectors)
            {
                collectorContributors[collector.Name] = [];
            }

            foreach (var wt in wageTypes)
            {
                // direct collector references
                if (wt.Collectors != null)
                {
                    foreach (var col in wt.Collectors)
                    {
                        if (collectorContributors.TryGetValue(col, out var list))
                        {
                            list.Add(new { wt.WageTypeNumber, wt.Name, via = "direct" });
                        }
                    }
                }

                // indirect via collector groups
                if (wt.CollectorGroups != null)
                {
                    foreach (var group in wt.CollectorGroups)
                    {
                        if (collectorGroupMap.TryGetValue(group, out var groupCollectors))
                        {
                            foreach (var col in groupCollectors)
                            {
                                if (collectorContributors.TryGetValue(col, out var list))
                                {
                                    list.Add(new { wt.WageTypeNumber, wt.Name, via = $"group:{group}" });
                                }
                            }
                        }
                    }
                }
            }

            var result = collectors.Select(c => new
            {
                c.Name,
                c.CollectMode,
                c.ValueType,
                c.Negated,
                c.Threshold,
                c.MinResult,
                c.MaxResult,
                c.CollectorGroups,
                c.Clusters,
                c.Attributes,
                contributors = collectorContributors.TryGetValue(c.Name, out var contributor) ? contributor : []
            });
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
