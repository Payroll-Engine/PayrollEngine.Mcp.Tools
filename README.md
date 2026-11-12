# Payroll Engine MCP Tools

> Part of the [Payroll Engine](https://github.com/Payroll-Engine/PayrollEngine) open-source payroll automation framework.
> Full documentation at [payrollengine.org](https://payrollengine.org).

Read-only MCP tools for the Payroll Engine — HR, Payroll, Report, System and Regulator query tools built on [PayrollEngine.Mcp.Core](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Core), ready for use in any MCP server deployment.

---

## NuGet Package

Available as a private GitHub Package:

```sh
dotnet add package PayrollEngine.Mcp.Tools
```

---

## Overview

`PayrollEngine.Mcp.Tools` provides the complete set of read-only MCP tools that expose Payroll Engine data to AI agents. All tools are **read-only by design** — no mutations, no side effects, with two exceptions: `get_employee_pay_preview` and `execute_payroll_report` execute synchronous calculations without persisting anything to the database.

Tools are organized into five roles. Each role is independently enabled or disabled per server deployment via `McpPermissions`.

---

## Tools

### Server — Always Available

Registered unconditionally, independent of role permissions or isolation level.

| Tool | Description |
|:-----|:------------|
| `get_server_info` | Server version, active isolation level, configured scope, and role permissions |

### HR — Human Resources

Employee master data, case values, and audit trail.

| Tool | Description |
|:-----|:------------|
| `list_divisions` | List all divisions of a tenant |
| `get_division` | Get a division by name |
| `get_division_attribute` | Get a single custom attribute of a division |
| `list_employees` | List employees with optional OData filter |
| `get_employee` | Get an employee by identifier |
| `get_employee_attribute` | Get a single custom attribute of an employee |
| `list_employee_case_values` | Full case value history of an employee |
| `list_company_case_values` | Company-level case values of a tenant |
| `list_employee_case_changes` | Audit trail of employee data mutations |
| `list_company_case_changes` | Audit trail of company data mutations |
| `get_available_cases` | Available cases for data entry within a payroll (script-evaluated per user) |

### Payroll — Payroll Processing

Payroll structure, regulation analysis, results, preview calculations, temporal case value queries, and lookup resolution.

| Tool | Description |
|:-----|:------------|
| `list_payrolls` | List all payrolls of a tenant |
| `get_payroll` | Get a payroll by name |
| `list_payruns` | List all payruns of a tenant |
| `list_payrun_jobs` | List all payrun jobs ordered by creation date descending |
| `get_payrun_statistics` | Payrun counts and employee totals for legal and forecast jobs within a date range |
| `list_payroll_cases` | Cases merged across regulation layers, filterable by type and cluster |
| `list_payroll_wage_types` | Effective wage types merged across all regulation layers |
| `list_payroll_collectors` | Collectors with their contributing wage types (direct and via groups) |
| `get_payroll_lookup_value` | Resolved lookup value by key or range value |
| `query_lookups` | List all lookups with metadata, filterable by attribute prefix |
| `get_lookup_values` | All values of a specific lookup, optionally at a historical regulation date |
| `compare_lookup_values` | Compare a lookup across all regulation cycles (validFrom dates) |
| `list_payroll_result_values` | Flat denormalized list of all result values |
| `get_consolidated_payroll_result` | All results for one employee and one period |
| `get_employee_pay_preview` | Preview payroll calculation without persisting results |
| `get_case_time_values` | Case values valid at a specific point in time |

### Report — Report Execution

| Tool | Description |
|:-----|:------------|
| `execute_payroll_report` | Execute a payroll report and return its result data set |

### System — Administration

| Tool | Description |
|:-----|:------------|
| `list_tenants` | List all tenants |
| `get_tenant` | Get a tenant by identifier |
| `get_tenant_attribute` | Get a single custom attribute of a tenant |
| `list_users` | List all users of a tenant |
| `get_user` | Get a user by identifier |
| `get_user_attribute` | Get a single custom attribute of a user |

### Regulator — Provenance Manifests

File based tools over the `lex.json` manifests of data regulations, built on [PayrollEngine.Lex](https://github.com/Payroll-Engine/PayrollEngine.Lex). They need no backend connection, read files below the configured workspace roots (`McpServer:Regulator:Roots`) and never write. They are registered only when `McpServer:Permissions:Regulator` is `Read` or higher; the default is `None`, so an existing deployment does not gain file access.

| Tool | Description |
|:-----|:------------|
| `lex_list_manifests` | Manifests with summary: entries, open entries, verification counts, snapshot state, content hash |
| `lex_get_entry` | One entry with sources, location, quote, verification record, exclusions and snapshot state |
| `lex_check` | Check against the data regulation; mode `development` or `release`, optional URL check |
| `lex_verify_snapshot` | Snapshot against manifest and evidence files |
| `lex_verify_source` | Rate every entry against its sources, `local` evidence or `live`; nothing is written |
| `lex_validate` | Structural validation of manifests |

The tools are implemented in the `RegulatorTools` folder (`LexQueryTools` class) and call [PayrollEngine.Lex](https://github.com/Payroll-Engine/PayrollEngine.Lex) directly (no `pecmd` subprocess).

The package references PayrollEngine.Lex with all assets, so the language files of the library (`LexVocabulary`) are copied to the output of the host project without an own reference. Hosts must list Lex dependencies in their own [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); this repository's notice covers MCP Tools dependencies only.

Every tool returns structured JSON. Limits per call:

- **Scope:** `root`, `repository` and `satellite` narrow the manifests of a call. Operations with network access (`lex_check` with `checkUrls`, `lex_verify_source` with source `live`) require a `repository` or `satellite`.
- **Maximum manifests:** `maxManifests` lowers the hard limit of the operation (list 200, snapshot and validation 100, check 50, release check 25, URL check 10, source verification 25, live source verification 5). The response reports `matched`, `processed`, `limit` and `truncated`.
- **Progress:** `lex_check` and `lex_verify_source` send progress notifications per manifest when the client requests them.

---

## Role × Isolation Level

`✓` = tools registered  `✗` = not applicable at this isolation level

| Role | MultiTenant | Tenant | Division | Employee |
|:-----|:-----------:|:------:|:--------:|:--------:|
| HR | ✓ | ✓ | ✓ | ✓ |
| Payroll | ✓ | ✓ | ✓ | ✗ |
| Report | ✓ | ✓ | ✗ | ✗ |
| System | ✓ | ✓ | ✗ | ✗ |
| Regulator | ✓ | ✓ | ✗ | ✗ |

---

## Assembly Marker

`ToolsMarker` is the assembly marker for tool discovery in `ToolRegistrar.GetPermittedTypes()`:

```csharp
var toolsAssembly = typeof(ToolsMarker).Assembly;
var permittedTypes = ToolRegistrar.GetPermittedTypes(toolsAssembly, permissions, isolationLevel);
```

When building a server that combines these tools with additional write tools from a private assembly, pass both assemblies to `GetPermittedTypes` or call it twice.

---

## Related Repositories

| Repository | Description |
|:-----------|:------------|
| [PayrollEngine.Mcp.Core](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Core) | Core infrastructure — `IsolationToolBase`, `ToolBase`, workspace isolation |
| [PayrollEngine.Lex](https://github.com/Payroll-Engine/PayrollEngine.Lex) | Provenance library used by the Regulator tools |
| [PayrollEngine.Mcp.Server](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Server) | Hosted MCP server using Core and Tools |
| [PayrollEngine.Client.Core](https://github.com/Payroll-Engine/PayrollEngine.Client.Core) | Payroll Engine REST API client |

---

## License

[MIT License](LICENSE) — free for personal and commercial use.
