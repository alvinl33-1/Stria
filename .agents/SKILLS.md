# Stria agent skills

One canonical folder, three readers:

```
.agents/skills/          ← the real files (Codex reads this path)
.claude/skills  → ../.agents/skills   (Claude Code)
.github/skills  → ../.agents/skills   (GitHub Copilot)
.agents/_disabled/       ← parked skills; no agent scans this folder
```

Add, edit or remove skills only in `.agents/skills/`. To enable a parked skill, move its folder from
`_disabled/` into `skills/`.

## Active skills

### Ship to Azure (Phase 3 — files only until Phase 3 is authorized)
| Skill | Use it for | Source |
|---|---|---|
| `compose-to-container-apps` | The plan: Compose services → Container Apps/jobs, managed Postgres, Key Vault, OIDC deploy. Start here. Repo map in `references/project-map.md` | custom |
| `azd-deployment` | `azure.yaml`, azd environments, remote builds, post-deploy checks | AAS library |
| `arm-templates` | Bicep modules, parameters, `what-if` | AAS library |
| `github-actions-advanced` | OIDC sign-in to Azure, hardened deploy job | AAS library |
| `azure-identity-dotnet` | `DefaultAzureCredential`, managed identity | AAS library |
| `azure-keyvault` | Secrets, keys, RBAC, rotation | AAS library |
| `dotnet-azure-runtime` | Code changes: passwordless Npgsql, Data Protection in Blob + Key Vault, forwarded headers, App Insights | custom |

### Security (Phase 2)
| Skill | Use it for | Source |
|---|---|---|
| `aspnet-entra-auth` | Microsoft.Identity.Web, role policies, data-plane scheme, antiforgery, rate limits, auth-matrix tests | custom |
| `threat-modeling-expert` | STRIDE review of the control/data-plane split | AAS library |

### Release and operations
| Skill | Use it for | Source |
|---|---|---|
| `pre-release-review` | Read-only go/no-go before a deploy | AAS library |
| `production-runtime-certification` | Evidence that a deployed revision is healthy | AAS library |
| `deployment-procedures` | Rollout and rollback principles | AAS library |
| `database-migrations-sql-migrations` | Zero-downtime migration planning (EF bundle job) | AAS library |
| `azure-monitor-audit` | Log Analytics, KQL, alert rules | AAS library |

### .NET engineering
| Skill | Use it for | Source |
|---|---|---|
| `csharp-pro` | Modern C# implementation and review | existing |
| `ef-core` | EF Core rules for this repo + reference guide | existing, now wrapped as a skill |
| `clean-architecture` | Layering/dependency review | existing, now wrapped as a skill |

### Agent orchestration
| Skill | Use it for | Source |
|---|---|---|
| `ask-copilot` | Ask GitHub Copilot CLI non-interactively | existing |
| `codex-delegate` | Hand a task to Codex CLI (folder renamed from `code-delegate` to match its `name`). Its `references/writing-the-brief.md` was never copied in, so that link is broken. | existing |

## Parked (`.agents/_disabled/`)
| Skill | Why it is parked |
|---|---|
| `hunt-session` | Was in `.github/security/`. It is an offensive bug-bounty session-hunting playbook that requires written authorization — not a general security skill. |
| `read-only-query-postgres` | Only a README; the scripts and config it describes are missing. |
| `azure-resource-manager-postgresql-dotnet` | Manages Postgres servers from C# (management SDK). Stria provisions with Bicep instead. |
