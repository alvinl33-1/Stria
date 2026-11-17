# Graph Report - Stria  (2026-11-17)

## Corpus Check
- Corpus is ~27,233 words - fits in a single context window. You may not need a graph.

## Summary
- 599 nodes · 993 edges · 42 communities (32 shown, 10 thin omitted)
- Extraction: 92% EXTRACTED · 8% INFERRED · 0% AMBIGUOUS · INFERRED: 77 edges (avg confidence: 0.84)
- Token cost: 189,760 input · 0 output

## Community Hubs (Navigation)
- Chaos Experiment Domain & API
- Namespaces & Framework Imports
- Docker Compose Stack
- Chaos Rules Validation & Tests
- Sample Service Data Plane
- DbContext & Test Fixtures
- EF Core Migrations
- System Analysis & Lifecycle
- NuGet Packages & SDKs
- Active Faults Dashboard
- Phase 2 Authorization Plan
- API Launch Settings
- Sample Service Launch Settings
- Dependabot Config
- Service Plan Roadmap
- CI Workflow Core
- CI Step Parameters
- README Fault Concepts
- Client Razor Imports
- Phase 1 Safety Handoff
- CI Step Definitions
- Dependency Review Job
- Reconnect Modal Script
- Fault Model & Rules
- CI Triggers
- Blazor App Shell
- Kill Switch Plan
- Client Router
- CI Environment Vars
- CI Quality Gates
- Container Build Job
- Dependency Audit Job
- Error Page
- IDE MCP Config
- Counter Page
- Weather Page
- Nav Menu
- Favicon

## God Nodes (most connected - your core abstractions)
1. `ChaosExperiment` - 36 edges
2. `ChaosRulesTests` - 35 edges
3. `Stria Phase 2 Plan` - 24 edges
4. `DatabaseChaosOrchestrator` - 19 edges
5. `ChaosApiTests` - 18 edges
6. `DatabaseChaosOrchestratorTests` - 17 edges
7. `PostgresFixture` - 17 edges
8. `StriaDbContext` - 15 edges
9. `FaultPoller` - 15 edges
10. `Stria Service Plan` - 15 edges

## Surprising Connections (you probably didn't know these)
- `Orchestrator Recheck` --references--> `DatabaseChaosOrchestrator`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.API/Services/DatabaseChaosOrchestrator.cs
- `Health Endpoints Outside Fault Path` --references--> `FaultInjectionMiddleware`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.SampleService/FaultInjectionMiddleware.cs
- `GET /api/v1/chaos/directives/{service}` --references--> `FaultDirective`  [INFERRED]
  README.md → Stria.API/DTOs/FaultDirective.cs
- `POST /api/v1/chaos/inject` --references--> `FaultInjectionRequest`  [INFERRED]
  README.md → Stria.API/DTOs/FaultInjectionRequest.cs
- `Partial Unique Constraint` --references--> `StriaDbContext`  [INFERRED]
  docs/PHASE_1_HANDOFF.md → Stria.API/Data/StriaDbContext.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Defence-in-Depth Fault Safety** — docs_phase_1_handoff_domain_rules_validation, docs_phase_1_handoff_orchestrator_recheck, docs_phase_1_handoff_partial_unique_constraint, docs_phase_1_handoff_automatic_lease_expiry, docs_phase_1_handoff_health_endpoints_outside_fault_path, stria_api_domain_chaosrules_stria_domain_chaosrules, stria_api_services_databasechaosorchestrator_stria_services_databasechaosorchestrator [EXTRACTED 1.00]
- **Phase 2 Credential Scheme Separation** — docs_phase_2_plan_browser_oidc_cookie_authentication, docs_phase_2_plan_jwt_bearer_api_client_authentication, docs_phase_2_plan_data_plane_authentication, docs_phase_2_plan_authorization_policies_chaos_view_chaos_operate_chaos_admin [INFERRED 0.85]
- **Directive Lease Lifecycle** — docs_stria_system_analysis_persistence_and_lifecycle, docs_stria_system_analysis_data_plane_behavior, stria_api_services_experimentexpiryservice_stria_services_experimentexpiryservice, stria_sampleservice_faultpoller_stria_sampleservice_faultpoller, stria_api_dtos_faultdirective_stria_dtos_faultdirective [INFERRED 0.85]

## Communities (42 total, 10 thin omitted)

### Community 0 - "Chaos Experiment Domain & API"
Cohesion: 0.07
Nodes (22): ChaosExperiments Table, POST /api/v1/chaos/inject, ChaosExperiment, ClearedAt, ClearedBy, CreatedAt, ExpiresAt, Id (+14 more)

### Community 1 - "Namespaces & Framework Imports"
Cohesion: 0.08
Nodes (17): Stria.Domain, Stria.Tests, Stria.Services, Stria.Data, Stria.Tests.TestSupport, Stria, Stria.DTOs, Microsoft.AspNetCore.Components.Forms (+9 more)

### Community 2 - "Docker Compose Stack"
Cohesion: 0.05
Nodes (42): context, dockerfile, condition, environment, healthcheck, image, restart, volumes (+34 more)

### Community 4 - "Sample Service Data Plane"
Cohesion: 0.08
Nodes (17): Stria.SampleService, Milestone 5 Supported Data-Plane Integration, Stria .NET Data-Plane Package, Target Acknowledgements, Data-Plane Behavior, Docker Compose Stack, sample-payment-gateway, FaultInjectionMiddleware (+9 more)

### Community 5 - "DbContext & Test Fixtures"
Cohesion: 0.08
Nodes (9): StriaDbContext, ChaosExperiments, Program, DockerRequirement, PostgresFixture, ConnectionString, ContextFactory, SimpleDbContextFactory (+1 more)

### Community 6 - "EF Core Migrations"
Cohesion: 0.09
Nodes (8): Stria.Data.Migrations, GET /api/v1/chaos/directives/{service}, GET /api/v1/chaos/faults, HTTP API, RFC 7807 Problem Details, InitialCreate, StriaDbContextModelSnapshot, FaultDirective

### Community 7 - "System Analysis & Lifecycle"
Cohesion: 0.09
Nodes (20): Open Phase 1 Review Findings, Milestone 1 Reliable Local Baseline, Current Architecture, Dashboard User Interface, Known Gaps and Risks, Persistence and Lifecycle, Recommended Enhancement Sequence, Runtime Topology (+12 more)

### Community 8 - "NuGet Packages & SDKs"
Cohesion: 0.08
Nodes (24): coverlet.collector (10.1.1), Microsoft.AspNetCore.Components.WebAssembly (10.0.14), Microsoft.AspNetCore.Components.WebAssembly.Server (10.0.14), Microsoft.AspNetCore.Mvc.Testing (10.0.14), Microsoft.EntityFrameworkCore (10.0.14), Microsoft.EntityFrameworkCore.Design (10.0.14), Microsoft.EntityFrameworkCore.Relational (10.0.14), Microsoft.Extensions.TimeProvider.Testing (10.11.0) (+16 more)

### Community 9 - "Active Faults Dashboard"
Cohesion: 0.10
Nodes (17): ActiveFaultsTable, ChaosFault, route:/, Stria.Client.Components, ChaosFault, ClearFault, DisposeAsync, FaultInjectionRequest (+9 more)

### Community 10 - "Phase 2 Authorization Plan"
Cohesion: 0.18
Nodes (14): Authorization Policies Chaos.View Chaos.Operate Chaos.Admin, Authorization Test Matrix, Browser OIDC Cookie Authentication, JWT Bearer API Client Authentication, Limits and Hardening, Microsoft Entra ID, Phase 2 Definition of Done, Risks and Decisions (+6 more)

### Community 11 - "API Launch Settings"
Cohesion: 0.12
Nodes (17): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, inspectUri, launchBrowser, applicationUrl (+9 more)

### Community 12 - "Sample Service Launch Settings"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 13 - "Dependabot Config"
Cohesion: 0.14
Nodes (13): applies-to, update-types, patterns, dotnet-minor-patch, github-actions, interval, updates, directory (+5 more)

### Community 14 - "Service Plan Roadmap"
Cohesion: 0.18
Nodes (10): Milestone 4 Service Registry and Experiment Lifecycle, Milestone 6 Operator Experience and Observability, Milestone 7 Delivery Pipeline and Hosted Demo, Product Goal Internal Resilience Platform, Release Gates, Scope and Safety Boundary, Service Plan Definition of Done, Service Registry (+2 more)

### Community 15 - "CI Workflow Core"
Cohesion: 0.20
Nodes (9): name, runs-on, timeout-minutes, concurrency, cancel-in-progress, group, jobs, build-and-test (+1 more)

### Community 16 - "CI Step Parameters"
Cohesion: 0.20
Nodes (10): with, cache, cache-dependency-path, comment-summary-in-pr, dotnet-version, fail-on-severity, if-no-files-found, name (+2 more)

### Community 17 - "README Fault Concepts"
Cohesion: 0.20
Nodes (8): CpuSpike Fault, Fault Types, Health and Readiness Endpoints, HttpFailure Fault, Latency Fault, Registered Targets, Sample Service Configuration, Stria README

### Community 18 - "Client Razor Imports"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Components.Forms, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web, Microsoft.AspNetCore.Components.Web.RenderMode, Microsoft.AspNetCore.Components.Web.Virtualization, Microsoft.JSInterop, Stria.Client, Stria.Client.Layout (+1 more)

### Community 19 - "Phase 1 Safety Handoff"
Cohesion: 0.25
Nodes (7): CpuSpike Known Limitation, Health Endpoints Outside Fault Path, Orchestrator Recheck, Phase 0 Local Correctness, Phase 1 Contract Testing and CI, Stria Phase 1 Handoff, Verification Evidence

### Community 20 - "CI Step Definitions"
Cohesion: 0.36
Nodes (9): steps, steps, steps, steps, if, name, run, shell (+1 more)

### Community 21 - "Dependency Review Job"
Cohesion: 0.22
Nodes (9): if, name, permissions, runs-on, timeout-minutes, dependency-review, permissions, contents (+1 more)

### Community 22 - "Reconnect Modal Script"
Cohesion: 0.32
Nodes (6): handleReconnectStateChanged(), reconnectModal, resumeButton, retry(), retryButton, retryWhenDocumentBecomesVisible()

### Community 23 - "Fault Model & Rules"
Cohesion: 0.29
Nodes (6): Domain Rules Validation, Milestone 2 Test and Contract Foundation, Fault Model, FaultType, ChaosRules, Services

### Community 24 - "CI Triggers"
Cohesion: 0.33
Nodes (6): on, pull_request, push, workflow_dispatch, branches, branches

### Community 25 - "Blazor App Shell"
Cohesion: 0.33
Nodes (5): HeadOutlet, ImportMap, ReconnectModal, ResourcePreloader, Routes

### Community 26 - "Kill Switch Plan"
Cohesion: 0.40
Nodes (4): Phase 2 Next Work, Authorized Control Plane Objective, Step 5 Admin Kill Switch, Milestone 3 Secure Control Plane

### Community 27 - "Client Router"
Cohesion: 0.40
Nodes (4): FocusOnNavigate, Found, Router, RouteView

### Community 28 - "CI Environment Vars"
Cohesion: 0.40
Nodes (5): env, DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_NOLOGO, DOTNET_SKIP_FIRST_TIME_EXPERIENCE, STRIA_REQUIRE_DOCKER_TESTS

### Community 29 - "CI Quality Gates"
Cohesion: 0.50
Nodes (3): Branch Protection build-and-test, CI Quality Gates, Testcontainers Integration Tests

### Community 30 - "Container Build Job"
Cohesion: 0.50
Nodes (4): name, runs-on, timeout-minutes, container-build

### Community 31 - "Dependency Audit Job"
Cohesion: 0.50
Nodes (4): name, runs-on, timeout-minutes, dependency-audit

### Community 32 - "Error Page"
Cohesion: 0.50
Nodes (3): OnInitialized, PageTitle, System.Diagnostics

## Knowledge Gaps
- **114 isolated node(s):** `ResourcePreloader`, `ImportMap`, `HeadOutlet`, `Routes`, `ReconnectModal` (+109 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 268 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ChaosExperiment` connect `Chaos Experiment Domain & API` to `DbContext & Test Fixtures`, `EF Core Migrations`, `Fault Model & Rules`?**
  _High betweenness centrality (0.052) - this node is a cross-community bridge._
- **Are the 9 inferred relationships involving `ChaosExperiment` (e.g. with `ChaosExperiments Table` and `DatabaseChaosOrchestrator.ApplyAsync()`) actually correct?**
  _`ChaosExperiment` has 9 INFERRED edges - model-reasoned connections that need verification._
- **What connects `ResourcePreloader`, `ImportMap`, `HeadOutlet` to the rest of the system?**
  _114 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Chaos Experiment Domain & API` be split into smaller, more focused modules?**
  _Cohesion score 0.06841046277665996 - nodes in this community are weakly interconnected._
- **Why does `FaultPoller` connect `Sample Service Data Plane` to `Namespaces & Framework Imports`, `EF Core Migrations`, `System Analysis & Lifecycle`, `Active Faults Dashboard`, `Phase 2 Authorization Plan`?**
  _High betweenness centrality (0.049) - this node is a cross-community bridge._
- **Should `Namespaces & Framework Imports` be split into smaller, more focused modules?**
  _Cohesion score 0.07610993657505286 - nodes in this community are weakly interconnected._
- **Why does `DatabaseChaosOrchestrator` connect `Chaos Experiment Domain & API` to `Namespaces & Framework Imports`, `Sample Service Data Plane`, `DbContext & Test Fixtures`, `System Analysis & Lifecycle`, `Phase 1 Safety Handoff`, `Kill Switch Plan`?**
  _High betweenness centrality (0.045) - this node is a cross-community bridge._