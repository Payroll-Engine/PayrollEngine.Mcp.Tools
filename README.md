# Payroll Engine MCP Tools

> Part of the [Payroll Engine](https://github.com/Payroll-Engine/PayrollEngine) open-source payroll automation framework.
> Full documentation at [payrollengine.org](https://payrollengine.org).

Read-only MCP tools for the Payroll Engine — HR, Payroll, Report and System query tools built on [PayrollEngine.Mcp.Core](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Core), ready for use in any MCP server deployment.

---

## NuGet Package

Available as a private GitHub Package:

```sh
dotnet add package PayrollEngine.Mcp.Tools
```

---

## Overview

`PayrollEngine.Mcp.Tools` provides the complete set of read-only MCP tools that expose Payroll Engine data to AI agents. All tools are **read-only by design** — no mutations, no side effects, with two exceptions: `get_employee_pay_preview` and `execute_payroll_report` execute synchronous calculations without persisting anything to the database.

Tools are organized into four roles. Each role is independently enabled or disabled per server deployment via `McpPermissions`.

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

### Payroll — Payroll Processing

Payroll structure, results, preview calculations, temporal case value queries, and lookup resolution.

| Tool | Description |
|:-----|:------------|
| `list_payrolls` | List all payrolls of a tenant |
| `get_payroll` | Get a payroll by name |
| `list_payruns` | List all payruns of a tenant |
| `list_payrun_jobs` | List all payrun jobs ordered by creation date descending |
| `list_payroll_wage_types` | Effective wage types merged across all regulation layers |
| `get_payroll_lookup_value` | Resolved lookup value by key or range value |
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

---

## Role × Isolation Level

`✓` = tools registered  `✗` = not applicable at this isolation level

| Role | MultiTenant | Tenant | Division | Employee |
|:-----|:-----------:|:------:|:--------:|:--------:|
| HR | ✓ | ✓ | ✓ | ✓ |
| Payroll | ✓ | ✓ | ✓ | ✗ |
| Report | ✓ | ✓ | ✗ | ✗ |
| System | ✓ | ✓ | ✗ | ✗ |

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
| [PayrollEngine.Mcp.Core](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Core) | Core infrastructure — `ToolBase`, isolation model, permission system |
| [PayrollEngine.Mcp.Server](https://github.com/Payroll-Engine/PayrollEngine.Mcp.Server) | Hosted MCP server using Core and Tools |
| [PayrollEngine.Client.Core](https://github.com/Payroll-Engine/PayrollEngine.Client.Core) | Payroll Engine REST API client |

---

## License

[MIT License](LICENSE) — free for personal and commercial use.
