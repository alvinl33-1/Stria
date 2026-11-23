# Graph Report - Stria  (2026-11-18)

## Corpus Check
- 85 files · ~57,415 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1046 nodes · 1788 edges · 79 communities (61 shown, 18 thin omitted)
- Extraction: 92% EXTRACTED · 8% INFERRED · 0% AMBIGUOUS · INFERRED: 145 edges (avg confidence: 0.84)
- Token cost: 325,685 input · 0 output

## Community Hubs (Navigation)
- Orchestrator and Experiment Model
- API Integration Test Harness
- Domain Rules and DTOs
- Deployment Procedures
- Data-Plane Auth Handler Types
- GitHub Actions Patterns
- NuGet Package References
- ASP.NET Core Namespaces
- Azure PostgreSQL and ARM Skills
- Compose to Container Apps Plan
- HTTP Hardening Middleware Types
- API Launch Settings
- Phase 2 Security Design
- EF Migrations Model
- Sample Service Launch Settings
- Persistence and DbContext
- Test Project Structure
- README API and Faults
- Clean Architecture Principles
- Sample Service Data Plane
- Dependabot Configuration
- Entra Auth Skill
- Active Faults Table
- Actor Resolution and Security
- PostgreSQL Test Fixture
- Phase 1 Safety Model
- Client Razor Imports
- Host Razor Imports
- Agent Delegation Skills
- Stria Server Environment
- API Domain and Services
- Phase 2 Handoff
- Service Plan Milestones
- Sample Fault State and Lease
- Agent Skills Index
- Dashboard Home Page
- EF Migration Files
- System Analysis
- Entra Setup Guide
- Antiforgery Endpoint Filter
- CI Workflow Jobs
- CI Step Settings
- SQL Migration Playbook
- Sample Service Environment
- Data-Plane Package Milestone
- CI Workflow Steps
- CI Job Settings
- Phase 2 Lessons Learned
- Postgres Compose Service
- Fault Injection Middleware
- Reconnect Modal Script
- EF Core Best Practices
- Client Routing and Auth
- Compose Service Dependencies
- Client API Services
- Own-Directive Authorization
- Chaos Authorization Policies
- CI Triggers
- Sample Service Options
- Database Migration Skills
- Compose Volumes
- Compose Health Check
- Static Asset Tests
- CI Environment Variables
- Root App Component
- Session Security Hunting
- Main Layout Sign-In
- Compose Build Context
- Container Build Job
- Dependency Audit Job
- Error Page
- IDE MCP Config
- Redirect to Login
- Access Denied Page
- Navigation Menu
- Favicon Asset

## God Nodes (most connected - your core abstractions)
1. `ChaosExperiment` - 38 edges
2. `ChaosRulesTests` - 35 edges
3. `Stria Phase 2 Plan - Authorized Control Plane` - 27 edges
4. `GitHub Actions Detailed Guide` - 23 edges
5. `ApiTestHost` - 21 edges
6. `DatabaseChaosOrchestrator` - 18 edges
7. `ApiSecurityTests` - 18 edges
8. `ChaosApiTests` - 17 edges
9. `DatabaseChaosOrchestratorTests` - 17 edges
10. `Stria Service Plan` - 15 edges

## Surprising Connections (you probably didn't know these)
- `Health Endpoints Outside Fault Path` --references--> `FaultInjectionMiddleware`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.SampleService/FaultInjectionMiddleware.cs
- `Orchestrator Recheck` --references--> `DatabaseChaosOrchestrator`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.API/Services/DatabaseChaosOrchestrator.cs
- `Domain Rules Validation` --references--> `ChaosRules`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.API/Domain/ChaosRules.cs
- `Fault Model` --references--> `ChaosRules`  [INFERRED]
  docs/STRIA_SYSTEM_ANALYSIS.md → Stria.API/Domain/ChaosRules.cs
- `Stria .NET Data-Plane Package` --references--> `FaultInjectionMiddleware`  [INFERRED]
  docs/STRIA_SERVICE_PLAN.md → Stria.SampleService/FaultInjectionMiddleware.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Defence-in-Depth Fault Safety** — docs_phase_1_handoff_domain_rules_validation, docs_phase_1_handoff_orchestrator_recheck, docs_phase_1_handoff_partial_unique_constraint, docs_phase_1_handoff_automatic_lease_expiry, docs_phase_1_handoff_health_endpoints_outside_fault_path, stria_api_domain_chaosrules_stria_domain_chaosrules, stria_api_services_databasechaosorchestrator_stria_services_databasechaosorchestrator [EXTRACTED 1.00]
- **Directive Lease Lifecycle** — docs_stria_system_analysis_persistence_and_lifecycle, docs_stria_system_analysis_data_plane_behavior, stria_api_services_experimentexpiryservice_stria_services_experimentexpiryservice, stria_sampleservice_faultpoller_stria_sampleservice_faultpoller, stria_api_dtos_faultdirective_stria_dtos_faultdirective [INFERRED 0.85]
- **Phase 2 credential separation: cookie, bearer, data-plane schemes** — docs_phase_2_plan_browser_authentication, docs_phase_2_plan_api_client_authentication, docs_phase_2_plan_data_plane_authentication, _agents_skills_aspnet_entra_auth_skill_explicit_schemes, readme_data_plane_keys [INFERRED 0.85]
- **Phase 2 work breakdown steps 0-6** — docs_phase_2_plan_step_0_ci, docs_phase_2_plan_step_1_identity_foundation, docs_phase_2_plan_step_2_route_authorization, docs_phase_2_plan_step_3_csrf_limits_headers, docs_phase_2_plan_step_4_data_plane_separation, docs_phase_2_plan_step_5_kill_switch, docs_phase_2_plan_step_6_verification [EXTRACTED 1.00]
- **Phase 3 Azure deployment skill set** — _agents_skills_compose_to_container_apps_skill, _agents_skills_compose_to_container_apps_references_project_map, _agents_skills_dotnet_azure_runtime_skill, _agents_skills_ef_core_skill_migrateonstartup_local_only, _agents_skills_compose_to_container_apps_references_project_map_stria_migrate_job, _agents_skills_dotnet_azure_runtime_skill_migration_bundle [INFERRED 0.85]
- **Passwordless Entra ID / Managed Identity Access Flow** — _agents_skills_azure_identity_dotnet_skill_defaultazurecredential, _agents_skills_azure_identity_dotnet_skill_managedidentitycredential, _agents_skills_azure_keyvault_skill_rbacauth, _agents_skills_azd_deployment_skill_managedidentityrbac [INFERRED 0.85]
- **Azure Diagnostic/Audit Logging Pipeline** — _agents_skills_azure_monitor_audit_skill_diagnosticsettings, _agents_skills_azure_monitor_audit_skill_kqlinvestigation, _agents_skills_azure_keyvault_skill_diagnosticlogging [INFERRED 0.85]
- **Release Lifecycle Gates: pre-release review, deploy, runtime certification** — _agents_skills_pre_release_review_skill, _agents_skills_deployment_procedures_skill, _agents_skills_production_runtime_certification_skill [INFERRED 0.85]
- **Runtime Certification Gate Sequence** — _agents_skills_production_runtime_certification_skill_deployment_identity, _agents_skills_production_runtime_certification_skill_database_migration_state, _agents_skills_production_runtime_certification_skill_liveness_readiness, _agents_skills_production_runtime_certification_skill_critical_routes, _agents_skills_production_runtime_certification_skill_operational_signals [EXTRACTED 1.00]
- **GitHub Actions Workflow Security Hardening** — _agents_skills_github_actions_advanced_skill_least_privilege_permissions, _agents_skills_github_actions_advanced_skill_sha_pinning, _agents_skills_github_actions_advanced_skill_script_injection_prevention, _agents_skills_github_actions_advanced_skill_pull_request_target_restriction, _agents_skills_github_actions_advanced_references_detailed_guide_oidc_authentication [INFERRED 0.85]

## Communities (79 total, 18 thin omitted)

### Community 0 - "Orchestrator and Experiment Model"
Cohesion: 0.06
Nodes (27): ChaosExperiments Table, Current Architecture, Actor, ChaosExperiment, ClearedAt, ClearedBy, ClearedByName, CreatedAt (+19 more)

### Community 1 - "API Integration Test Harness"
Cohesion: 0.05
Nodes (13): ChaosApiClient, AntiforgeryTokenResponse, ApiSecurityTests, MeResponse, DataPlaneSecurityTests, HardeningTests, RateLimitTests, ApiTestHost (+5 more)

### Community 2 - "Domain Rules and DTOs"
Cohesion: 0.09
Nodes (9): Stria.DTOs, Domain Rules Validation, Milestone 2 Test and Contract Foundation, ChaosRules, Services, ValidatedFault, FaultInjectionRequest, ChaosRulesTests (+1 more)

### Community 3 - "Deployment Procedures"
Cohesion: 0.06
Nodes (36): Deployment Procedures Skill, Deployment Anti-Patterns, Emergency Procedures, 5-Phase Deployment Process, Platform Selection Decision Tree, Post-Deployment Verification Window, Pre-Deployment Checklist (4 Verification Categories), Zero-Downtime Strategies (Rolling, Blue-Green, Canary) (+28 more)

### Community 4 - "Data-Plane Auth Handler Types"
Cohesion: 0.08
Nodes (6): DataPlaneAuthenticationHandler, DataPlaneKeys, DataPlaneOptions, ServiceKeyHashes, ClaimMappingTests, TestCookieHandler

### Community 5 - "GitHub Actions Patterns"
Cohesion: 0.10
Nodes (22): GitHub Actions Detailed Guide, Automated Release with Changelog Pattern, Azure Federated Identity Login, Build-Test-Push-Deploy Pipeline Pattern, Caching Strategies, Common Pitfalls and Fixes, Composite Actions, Concurrency Groups (+14 more)

### Community 6 - "NuGet Package References"
Cohesion: 0.09
Nodes (23): coverlet.collector (10.1.1), Microsoft.AspNetCore.Authentication.JwtBearer (10.0.14), Microsoft.AspNetCore.Authentication.OpenIdConnect (10.0.14), Microsoft.AspNetCore.Components.WebAssembly (10.0.14), Microsoft.AspNetCore.Components.WebAssembly.Authentication (10.0.14), Microsoft.AspNetCore.Components.WebAssembly.Server (10.0.14), Microsoft.AspNetCore.Mvc.Testing (10.0.14), Microsoft.EntityFrameworkCore (10.0.14) (+15 more)

### Community 8 - "Azure PostgreSQL and ARM Skills"
Cohesion: 0.09
Nodes (21): Azure.ResourceManager.PostgreSql (.NET) Skill, PostgreSQL Flexible Server Management, Read-Only PostgreSQL Query Skill, ARM Templates Details Reference, ARM Templates & Bicep Skill, Bicep Infrastructure as Code, Key Vault Reference in Parameter Files, What-If Deployment Preview (+13 more)

### Community 9 - "Compose to Container Apps Plan"
Cohesion: 0.10
Nodes (18): Stria Compose to Container Apps project map, Azure configuration sources, Existing behaviour to preserve (HTTPS exemption, lease retention), Phase guard: files only until Phase 3, stria-server container app, compose-to-container-apps skill, Cost notes (budget alert, tear down), Compose to Container Apps mapping rules (+10 more)

### Community 10 - "HTTP Hardening Middleware Types"
Cohesion: 0.12
Nodes (6): ChaosRateLimitOptions, DataPlanePermitLimit, MutatePermitLimit, ReadPermitLimit, WindowSeconds, HttpHardening

### Community 11 - "API Launch Settings"
Cohesion: 0.12
Nodes (17): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, inspectUri, launchBrowser, applicationUrl (+9 more)

### Community 12 - "Phase 2 Security Design"
Cohesion: 0.15
Nodes (11): Auth matrix tests via StriaApiFactory test handler, HttpHardening / CookieAntiforgeryFilter, Stria Phase 2 Plan - Authorized Control Plane, Phase 2 definition of done, Limits and hardening (3.6), Phase 2 objective: authorized, auditable fault injection, PHASE_1_HANDOFF.md, Risks and decisions (+3 more)

### Community 13 - "EF Migrations Model"
Cohesion: 0.19
Nodes (3): InitialCreate, AddActorDisplayNames, StriaDbContextModelSnapshot

### Community 14 - "Sample Service Launch Settings"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 15 - "Persistence and DbContext"
Cohesion: 0.15
Nodes (8): Persistence and Lifecycle, StriaDbContext, ChaosExperiments, ExperimentRetentionOptions, MaxAgeDays, MaxClearedExperiments, SweepIntervalMinutes, ExperimentRetentionService

### Community 16 - "Test Project Structure"
Cohesion: 0.36
Nodes (4): Stria.Tests, Stria.Client.Security, Stria.Tests.TestSupport, TestDataPlaneKeys

### Community 17 - "README API and Faults"
Cohesion: 0.16
Nodes (14): CpuSpike gap (remove in Phase 5), Stria README, Antiforgery X-XSRF-TOKEN for cookie POSTs, CpuSpike fault (recorded, not executed), GET /api/v1/chaos/directives/{service}, Docker Compose stack (Postgres, Stria, sample service), Entra app roles (ChaosViewer/Operator/Admin), Experiment lease (1-3600 s, default 300) (+6 more)

### Community 18 - "Clean Architecture Principles"
Cohesion: 0.16
Nodes (12): Clean Architecture deep reference, Boundaries, Clean Architecture (Robert C. Martin, 2017), Component cohesion and coupling (REP, CCP, CRP, ADP, SDP, SAP), Inversion of dependencies, Layers: entities, use cases, interface adapters, frameworks, SOLID principles, clean-architecture skill (+4 more)

### Community 20 - "Dependabot Configuration"
Cohesion: 0.14
Nodes (13): applies-to, update-types, patterns, dotnet-minor-patch, github-actions, interval, updates, directory (+5 more)

### Community 21 - "Entra Auth Skill"
Cohesion: 0.18
Nodes (11): aspnet-entra-auth skill, Authorization policies incl. Chaos.DataPlane, CSRF, body limits, rate limits, forwarded headers, OwnDirectiveRequirement, Sample service credential (hashed key Phase 2, managed identity Phase 3), Trusted actor from oid claim, ActorResolver, ChaosPolicies (+3 more)

### Community 22 - "Active Faults Table"
Cohesion: 0.15
Nodes (12): ChaosFault, IAsyncDisposable, ChaosFault, ClearFault, DisposeAsync, FaultInjectionRequest, OnAfterRender, PollFaults (+4 more)

### Community 24 - "PostgreSQL Test Fixture"
Cohesion: 0.19
Nodes (5): DockerRequirement, PostgresFixture, ConnectionString, ContextFactory, SimpleDbContextFactory

### Community 25 - "Phase 1 Safety Model"
Cohesion: 0.17
Nodes (10): Branch Protection build-and-test, CpuSpike Known Limitation, Health Endpoints Outside Fault Path, Orchestrator Recheck, Phase 0 Local Correctness, Phase 1 Contract Testing and CI, Phase 2 Next Work, Stria Phase 1 Handoff (+2 more)

### Community 26 - "Client Razor Imports"
Cohesion: 0.15
Nodes (12): Microsoft.AspNetCore.Components.Authorization, Stria.Client.Components, Microsoft.AspNetCore.Authorization, Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web, Microsoft.AspNetCore.Components.Web.RenderMode, Microsoft.AspNetCore.Components.Web.Virtualization (+4 more)

### Community 27 - "Host Razor Imports"
Cohesion: 0.15
Nodes (12): Stria, Microsoft.AspNetCore.Authorization, Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web, Microsoft.AspNetCore.Components.Web.RenderMode, Microsoft.AspNetCore.Components.Web.Virtualization, Microsoft.JSInterop (+4 more)

### Community 28 - "Agent Delegation Skills"
Cohesion: 0.21
Nodes (10): ask-copilot skill, Minimal permission flags (no --yolo / --allow-all-paths), Copilot session management (--name/--resume), codex-delegate skill, Delegation authorization model (surface, don't absorb; stop for scope changes), amElnagdy/delegate-skills (MIT), Brief-dispatch-wait-review-land loop, openai-codex Claude Code plugin (+2 more)

### Community 29 - "Stria Server Environment"
Cohesion: 0.17
Nodes (12): AzureAd__ClientId, AzureAd__ClientSecret, AzureAd__TenantId, AzureAdApi__ClientId, AzureAdApi__TenantId, ConnectionStrings__Stria, Database__MigrateOnStartup, DataPlane__ServiceKeyHashes__PaymentGateway (+4 more)

### Community 30 - "API Domain and Services"
Cohesion: 0.32
Nodes (3): Stria.Domain, Stria.Services, Stria.Data

### Community 31 - "Phase 2 Handoff"
Cohesion: 0.18
Nodes (10): Stria Phase 2 Handoff, DataPlaneAuthentication, Follow-ups (blazor.web.js CI check, enforce CSP, dependency-review required, untrack .idea), KillSwitchEvents append-only table, Local environment state (test tenant, test accounts), Start here next session checklist, Status: Steps 0-4 merged (PR #14, PR #15), Verification evidence (88 tests, CI at b07c0a0) (+2 more)

### Community 32 - "Service Plan Milestones"
Cohesion: 0.17
Nodes (11): Milestone 1 Reliable Local Baseline, Milestone 4 Service Registry and Experiment Lifecycle, Milestone 6 Operator Experience and Observability, Milestone 7 Delivery Pipeline and Hosted Demo, Product Goal Internal Resilience Platform, Release Gates, Scope and Safety Boundary, Service Plan Definition of Done (+3 more)

### Community 33 - "Sample Fault State and Lease"
Cohesion: 0.23
Nodes (3): FaultState, Lease, FaultDirective

### Community 34 - "Agent Skills Index"
Cohesion: 0.18
Nodes (10): Stria agent skills index, AAS library skills (azd-deployment, arm-templates, github-actions-advanced, azure-identity-dotnet, azure-keyvault, etc.), Canonical .agents/skills folder with symlinks for Claude/Copilot, csharp-pro skill, BenchmarkDotNet measured results, Modern C# focus areas (records, nullable, async, testing), threat-modeling-expert skill, Attack tree construction (+2 more)

### Community 35 - "Dashboard Home Page"
Cohesion: 0.18
Nodes (9): Home, route:/, CommitChaos, FaultInjectionRequest, ActiveFaultsTable, Authorized, AuthorizeView, NotAuthorized (+1 more)

### Community 37 - "System Analysis"
Cohesion: 0.18
Nodes (9): Dashboard User Interface, Fault Model, Known Gaps and Risks, Recommended Enhancement Sequence, Runtime Topology, Stria System Analysis, Working Conventions, FaultType (+1 more)

### Community 38 - "Entra Setup Guide"
Cohesion: 0.27
Nodes (7): Microsoft Entra setup (local development), Stria (local) app registration, App roles ChaosViewer/ChaosOperator/ChaosAdmin, Chaos.Access API scope, Client-credentials automation flow, Redirect URIs (signin-oidc), User-secrets configuration (AzureAd/AzureAdApi)

### Community 40 - "CI Workflow Jobs"
Cohesion: 0.20
Nodes (9): name, runs-on, timeout-minutes, concurrency, cancel-in-progress, group, jobs, build-and-test (+1 more)

### Community 41 - "CI Step Settings"
Cohesion: 0.20
Nodes (10): with, cache, cache-dependency-path, comment-summary-in-pr, dotnet-version, fail-on-severity, if-no-files-found, name (+2 more)

### Community 42 - "SQL Migration Playbook"
Cohesion: 0.22
Nodes (8): SQL migration implementation playbook, Batch processing and parallel migration, Blue-green schema migration (dual-write trigger), Data integrity validation, Flyway and Alembic migration scripts, Online schema change (NOT VALID constraint), Rollback procedures, Batch operations (ExecuteUpdate/ExecuteDelete, bulk insert)

### Community 43 - "Sample Service Environment"
Cohesion: 0.22
Nodes (9): Stria__BaseUrl, Stria__PollIntervalMs, Stria__ServiceKey, Stria__ServiceName, depends_on, environment, ports, restart (+1 more)

### Community 44 - "Data-Plane Package Milestone"
Cohesion: 0.28
Nodes (4): Milestone 5 Supported Data-Plane Integration, Stria .NET Data-Plane Package, Target Acknowledgements, FaultPoller

### Community 45 - "CI Workflow Steps"
Cohesion: 0.36
Nodes (9): steps, steps, steps, steps, if, name, run, shell (+1 more)

### Community 46 - "CI Job Settings"
Cohesion: 0.22
Nodes (9): if, name, permissions, runs-on, timeout-minutes, dependency-review, permissions, contents (+1 more)

### Community 47 - "Phase 2 Lessons Learned"
Cohesion: 0.25
Nodes (4): RoleClaimType = roles, Entra troubleshooting (AADSTS50105, AADSTS50011, role caching), Lessons from Phase 2, ClaimMappingTests

### Community 48 - "Postgres Compose Service"
Cohesion: 0.25
Nodes (8): environment, image, restart, volumes, POSTGRES_DB, POSTGRES_PASSWORD, POSTGRES_USER, db

### Community 50 - "Reconnect Modal Script"
Cohesion: 0.32
Nodes (6): handleReconnectStateChanged(), reconnectModal, resumeButton, retry(), retryButton, retryWhenDocumentBecomesVisible()

### Community 51 - "EF Core Best Practices"
Cohesion: 0.29
Nodes (7): Index management (CREATE INDEX CONCURRENTLY), EF Core best practices reference, EF Core anti-patterns, Concurrency (row versioning) and explicit transactions / outbox, Connection management and DbContext pooling, Indexing strategy, Query optimization (AsNoTracking, projection, eager loading, split & compiled queries)

### Community 52 - "Client Routing and Auth"
Cohesion: 0.29
Nodes (6): AuthorizeRouteView, FocusOnNavigate, Found, RedirectToLogin, Router, NotAuthorized

### Community 53 - "Compose Service Dependencies"
Cohesion: 0.29
Nodes (7): condition, db, stria-server, container_name, depends_on, ports, volumes

### Community 57 - "CI Triggers"
Cohesion: 0.33
Nodes (6): on, pull_request, push, workflow_dispatch, branches, branches

### Community 58 - "Sample Service Options"
Cohesion: 0.33
Nodes (6): StriaOptions, BaseUrl, MaxConcurrentDelayed, PollIntervalMs, ServiceKey, ServiceName

### Community 59 - "Database Migration Skills"
Cohesion: 0.40
Nodes (5): database-migrations-sql-migrations skill, Migration output format (analysis, plan, scripts, validation, rollback), ef-core skill, Database:MigrateOnStartup local-only, EF Core rules for Stria (AsNoTracking, CancellationToken, migrations)

### Community 60 - "Compose Volumes"
Cohesion: 0.40
Nodes (4): services, volumes, stria-dpkeys, stria-pgdata

### Community 61 - "Compose Health Check"
Cohesion: 0.40
Nodes (5): healthcheck, interval, retries, test, timeout

### Community 63 - "CI Environment Variables"
Cohesion: 0.40
Nodes (5): env, DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_NOLOGO, DOTNET_SKIP_FIRST_TIME_EXPERIENCE, STRIA_REQUIRE_DOCKER_TESTS

### Community 64 - "Root App Component"
Cohesion: 0.40
Nodes (4): HeadOutlet, ImportMap, ResourcePreloader, Routes

### Community 65 - "Session Security Hunting"
Cohesion: 0.50
Nodes (3): Hunt-Session Vulnerability Skill, Refresh-Token Rotation and Reuse-Detection, Session Fixation

### Community 66 - "Main Layout Sign-In"
Cohesion: 0.50
Nodes (3): AntiforgeryToken, Authorized, AuthorizeView

### Community 67 - "Compose Build Context"
Cohesion: 0.67
Nodes (4): context, dockerfile, build, build

### Community 68 - "Container Build Job"
Cohesion: 0.50
Nodes (4): name, runs-on, timeout-minutes, container-build

### Community 69 - "Dependency Audit Job"
Cohesion: 0.50
Nodes (4): name, runs-on, timeout-minutes, dependency-audit

### Community 70 - "Error Page"
Cohesion: 0.50
Nodes (3): OnInitialized, PageTitle, System.Diagnostics

## Knowledge Gaps
- **216 isolated node(s):** `Services`, `ConnectionString`, `ContextFactory`, `SweepIntervalSeconds`, `MaxAgeDays` (+211 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 427 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **18 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Stria Service Plan` connect `Service Plan Milestones` to `Domain Rules and DTOs`, `System Analysis`, `Data-Plane Package Milestone`, `Phase 1 Safety Model`, `Phase 2 Handoff`?**
  _High betweenness centrality (0.112) - this node is a cross-community bridge._
- **Are the 9 inferred relationships involving `ChaosExperiment` (e.g. with `ChaosExperiments Table` and `DatabaseChaosOrchestrator.ApplyAsync()`) actually correct?**
  _`ChaosExperiment` has 9 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Services`, `ConnectionString`, `ContextFactory` to the rest of the system?**
  _216 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Orchestrator and Experiment Model` be split into smaller, more focused modules?**
  _Cohesion score 0.05714285714285714 - nodes in this community are weakly interconnected._
- **Why does `Global kill switch (3.7)` connect `Phase 2 Handoff` to `Phase 2 Security Design`?**
  _High betweenness centrality (0.100) - this node is a cross-community bridge._
- **Should `API Integration Test Harness` be split into smaller, more focused modules?**
  _Cohesion score 0.054340396445659606 - nodes in this community are weakly interconnected._
- **Why does `Milestone 3 Secure Control Plane` connect `Phase 2 Handoff` to `Service Plan Milestones`?**
  _High betweenness centrality (0.100) - this node is a cross-community bridge._