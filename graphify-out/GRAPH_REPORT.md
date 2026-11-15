# Graph Report - VerticalSliceSubsDotNetAOT  (2026-11-15)

## Corpus Check
- 271 files · ~108,518 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 16 file(s) not represented in the graph (top: (none) 8, .props 1, .http 1)

## Summary
- 3254 nodes · 6753 edges · 203 communities (192 shown, 11 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 622 edges (avg confidence: 0.85)
- Token cost: 113,916 input · 0 output

## Community Hubs (Navigation)
- Payments Event Sourcing
- AppHost & DbUp Migrations
- Host Setup & Telemetry
- API Slices: Cancel/Settle/Plans
- Relay Outbox & Wolverine
- Billing gRPC Contract
- API Integration Tests
- compose.yaml Stack
- API Slices: Lifecycle
- Kong & OTel Collector Config
- Grafana Datasources
- Error Handling & Health
- Test Host & Renewal Client
- Subscription Aggregate
- Health Probe & Broker Tests
- Helm Values (Local)
- Argo CD Values
- Taskfile Tasks
- Projects & Testcontainers
- Invoice Aggregate
- Locust Load Tests
- Subscription Handlers
- Relay API Client
- Invoice Endpoints
- Invoice Queries
- Relay Feature Namespaces
- Loki Config
- Cache & Concurrency
- SonarQube Compose
- Billing Features
- Community 30
- Community 31
- Community 32
- Community 33
- Community 34
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65
- Community 66
- Community 67
- Community 68
- Community 69
- Community 70
- Community 71
- Community 72
- Community 73
- Community 74
- Community 75
- Community 76
- Community 77
- Community 78
- Community 79
- Community 80
- Community 81
- Community 82
- Community 83
- Community 84
- Community 85
- Community 86
- Community 87
- Community 88
- Community 89
- Community 90
- Community 91
- Community 92
- Community 93
- Community 94
- Community 95
- Community 96
- Community 97
- Community 98
- Community 99
- Community 100
- Community 101
- Community 102
- Community 103
- Community 104
- Community 105
- Community 106
- Community 107
- Community 108
- Community 109
- Community 110
- Community 111
- Community 112
- Community 113
- Community 114
- Community 115
- Community 117
- Community 118
- Community 119
- Community 120
- Community 121
- Community 122
- Community 123
- Community 124
- Community 125
- Community 126
- Community 127
- Community 128
- Community 129
- Community 130
- Community 131
- Community 132
- Community 133
- Community 134
- Community 135
- Community 136
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 145
- Community 146
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154
- Community 155
- Community 156
- Community 157
- Community 158
- Community 159
- Community 160
- Community 161
- Community 162
- Community 163
- Community 164
- Community 165
- Community 166
- Community 167
- Community 168
- Community 169
- Community 170
- Community 171
- Community 172
- Community 173
- Community 174
- Community 175
- Community 176
- Community 177
- Community 178
- Community 179
- Community 180
- Community 181
- Community 182
- Community 183
- Community 184
- Community 185
- Community 186
- Community 187
- Community 188
- Community 189
- Community 190
- Community 191
- Community 192
- Community 193
- Community 194
- Community 195
- Community 196
- Community 197
- Community 198
- Community 199
- Community 200
- Community 201
- Community 202

## God Nodes (most connected - your core abstractions)
1. `tasks` - 74 edges
2. `ApiProblemException` - 46 edges
3. `SubscriptionManagement.Infrastructure` - 45 edges
4. `vars` - 37 edges
5. `SubscriptionManagement.Contracts` - 34 edges
6. `RenewalSaga` - 33 edges
7. `ApiFixture` - 32 edges
8. `Invoice` - 31 edges
9. `Payment` - 30 edges
10. `SystemFixture` - 30 edges

## Surprising Connections (you probably didn't know these)
- `Raw SQL transactional outbox` --semantically_similar_to--> `Marten event stream with inline snapshot`  [INFERRED] [semantically similar]
  README.md → docs/adr/007-event-sourced-payments.md
- `Java Spring Boot reference implementation` --conceptually_related_to--> `Subscription API (Native AOT)`  [INFERRED]
  docs/migration/java-feature-gap.md → README.md
- `Startup, memory, image and throughput results` --rationale_for--> `Native AOT accepted constraints`  [INFERRED]
  docs/performance.md → README.md
- `At-least-once delivery with idempotent handlers` --semantically_similar_to--> `Pure decider with optimistic concurrency`  [INFERRED] [semantically similar]
  README.md → docs/adr/007-event-sourced-payments.md
- `ADR-001 Native AOT with Minimal APIs` --rationale_for--> `Subscription API (Native AOT)`  [INFERRED]
  docs/adr/001-native-aot.md → README.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Renewal saga across API, Relay, Billing, Payments and RabbitMQ** — readme_api, readme_relay, readme_billing, readme_payments, readme_messaging, readme_renewal_saga [EXTRACTED 1.00]
- **JIT services isolated from the Native AOT API** — readme_billing, readme_payments, readme_relay, docs_adr_005_jit_messaging_services_jit_boundary, docs_adr_001_native_aot_adr_001 [EXTRACTED 1.00]
- **Local GitOps delivery pipeline (registry, Helm, Argo CD, Rollouts canary)** — readme_registry, readme_helm, readme_argo, readme_canary, readme_kong [EXTRACTED 1.00]

## Communities (203 total, 11 thin omitted)

### Community 0 - "Payments Event Sourcing"
Cohesion: 0.06
Nodes (37): CancelPayment, PaymentRefunded, Payment, Amount, Currency, Id, InvoiceId, PeriodEnd (+29 more)

### Community 1 - "AppHost & DbUp Migrations"
Cohesion: 0.05
Nodes (13): AppHost, LoadTests, Observability, ObservabilityStack, PostgresCluster, S2SServices, Services, Settings (+5 more)

### Community 2 - "Host Setup & Telemetry"
Cohesion: 0.06
Nodes (12): CreatePlanCommand, CreatePlanValidator, CreateSubscriptionCommand, CreateSubscriptionValidator, ValidationErrors, RenewalQuote, BillingOptions, BillingOptionsTests (+4 more)

### Community 3 - "API Slices: Cancel/Settle/Plans"
Cohesion: 0.05
Nodes (33): SubscriptionManagement.Features.Subscriptions.CancelSubscriptions, SubscriptionManagement.Features.Invoices.SettleInvoice, SubscriptionManagement.Features.Plans.ListPlans, SubscriptionManagement.Features.Subscriptions, SubscriptionManagement.Features.Plans, SubscriptionManagement.Features.Invoices.PayInvoice, SubscriptionManagement.Features.Subscriptions.CreateSubscriptions, SubscriptionManagement.Infrastructure (+25 more)

### Community 4 - "Relay Outbox & Wolverine"
Cohesion: 0.07
Nodes (16): SubscriptionCanceled, OutboxDrain, OutboxRow, RelayOptions, BatchSize, PollIntervalMs, OutboxDrainTests, Published (+8 more)

### Community 5 - "Billing gRPC Contract"
Cohesion: 0.06
Nodes (17): GrpcBillingGateway, BillingGrpcService, BillingGrpcServiceTests, BillingStub, Address, QuoteReply, QuoteRequests, RenewalReply (+9 more)

### Community 6 - "API Integration Tests"
Cohesion: 0.16
Nodes (6): ApiTestBase, Client, Fixture, CacheTests, LifecycleApiTests, RenewalApiTests

### Community 7 - "compose.yaml Stack"
Cohesion: 0.05
Nodes (48): build, depends_on, environment, healthcheck, image, ports, restart, context (+40 more)

### Community 8 - "API Slices: Lifecycle"
Cohesion: 0.05
Nodes (23): SubscriptionManagement.Features.Subscriptions.ResumeSubscription, SubscriptionManagement.Features.Invoices.VoidInvoice, SubscriptionManagement.Features.Subscriptions.GetSubscription, SubscriptionManagement.Features.Subscriptions.StartRenewal, SubscriptionManagement.Features.Subscriptions.PauseSubscription, SubscriptionManagement.Features.Subscriptions.RenewSubscription, VoidInvoiceEndpoint, VoidInvoiceHandler (+15 more)

### Community 9 - "Kong & OTel Collector Config"
Cohesion: 0.05
Nodes (48): kubernetes.io/ingress.class, bandwidth_metrics, latency_metrics, propagation, resource_attributes, status_code_metrics, traces_endpoint, upstream_health_metrics (+40 more)

### Community 10 - "Grafana Datasources"
Cohesion: 0.04
Nodes (47): apiVersion, datasources, access, isDefault, jsonData, name, type, uid (+39 more)

### Community 11 - "Error Handling & Health"
Cohesion: 0.07
Nodes (7): ApiExceptionHandler, CorrelationMiddleware, HealthEndpoints, ProblemResult, ProblemWriter, ErrorHandlingTests, ProblemWriterTests

### Community 12 - "Test Host & Renewal Client"
Cohesion: 0.12
Nodes (13): CancellationChoreographyTests, RenewalSagaSystemTests, SystemFixture, Api, ApiConnectionString, ApiFactory, Billing, Payments (+5 more)

### Community 13 - "Subscription Aggregate"
Cohesion: 0.09
Nodes (17): BilledPeriod, Subscription, CurrentPeriodEnd, CurrentPeriodStart, Id, Price, ServiceName, StartDate (+9 more)

### Community 14 - "Health Probe & Broker Tests"
Cohesion: 0.08
Nodes (4): HealthProbe, PlatformTests, PaymentEndpointTests, HealthProbeTests

### Community 15 - "Helm Values (Local)"
Cohesion: 0.05
Nodes (41): autoscaling, enabled, maxReplicas, minReplicas, config, environment, minute, openApiEnabled (+33 more)

### Community 16 - "Argo CD Values"
Cohesion: 0.06
Nodes (40): applicationSet, replicas, exec.enabled, timeout.reconciliation, timeout.reconciliation.jitter, configs, cm, params (+32 more)

### Community 17 - "Taskfile Tasks"
Cohesion: 0.05
Nodes (40): sh, dotenv, vars, ALLOY_CHART_VERSION, API_HOST, API_IMAGE, API_REPO, APP_NAME (+32 more)

### Community 18 - "Projects & Testcontainers"
Cohesion: 0.10
Nodes (9): SubscriptionManagement.Migrator, Microsoft.Extensions.Hosting, SubscriptionManagement.Payments.Tests, ServiceDefaultsExtensions, BillingCollection, ApiCollection, PaymentsCollection, RelayCollection (+1 more)

### Community 19 - "Invoice Aggregate"
Cohesion: 0.10
Nodes (18): Invoice, Amount, Currency, Id, IssuedAt, PaidAt, PaymentId, PeriodEnd (+10 more)

### Community 20 - "Locust Load Tests"
Cohesion: 0.06
Nodes (27): body(), StepShape, SubscriptionUser, cmds, desc, prompt, cmds, desc (+19 more)

### Community 21 - "Subscription Handlers"
Cohesion: 0.13
Nodes (12): CancelSubscriptionCommand, GetSubscriptionQuery, PauseSubscriptionCommand, RenewSubscriptionCommand, RenewSubscriptionResult, ResumeSubscriptionCommand, StartRenewalCommand, SubscriptionCache (+4 more)

### Community 22 - "Relay API Client"
Cohesion: 0.12
Nodes (10): ApiClient, SettleInvoiceRequest, CancelRenewalHandlerTests, PausePath, VoidPath, CommandHandlingTests, SettleInvoiceHandlerTests, Call (+2 more)

### Community 23 - "Invoice Endpoints"
Cohesion: 0.08
Nodes (14): GetInvoiceQuery, PayInvoiceCommand, SettleInvoiceCommand, VoidInvoiceCommand, GetPlanQuery, Plan, PlanInterval, Month (+6 more)

### Community 24 - "Invoice Queries"
Cohesion: 0.16
Nodes (5): InvoiceList, InvoiceResponse, InvoiceStore, PlanStore, SubscriptionStore

### Community 25 - "Relay Feature Namespaces"
Cohesion: 0.09
Nodes (9): SubscriptionManagement.Relay.Features.Renewals.Cancel, SubscriptionManagement.Relay.Features.Invoices.Settle, SubscriptionManagement.Relay.Infrastructure, SubscriptionManagement.Relay.Features.Outbox, SubscriptionManagement.Relay, SubscriptionManagement.Billing, SubscriptionManagement.Payments, SettleInvoiceHandler (+1 more)

### Community 26 - "Loki Config"
Cohesion: 0.06
Nodes (34): analytics, reporting_enabled, auth_enabled, common, instance_addr, path_prefix, replication_factor, ring (+26 more)

### Community 27 - "Cache & Concurrency"
Cohesion: 0.09
Nodes (7): CacheHealthCheck, DatabaseHealthCheck, DatabaseHealthCheck, DatabaseHealthCheck, DatabaseHealthCheck, ServiceSetup, BrokenCache

### Community 28 - "SonarQube Compose"
Cohesion: 0.06
Nodes (33): sonarqube-db, POSTGRES_DB, POSTGRES_PASSWORD, POSTGRES_USER, SONAR_ES_BOOTSTRAP_CHECKS_DISABLE, SONAR_JDBC_PASSWORD, SONAR_JDBC_URL, SONAR_JDBC_USERNAME (+25 more)

### Community 29 - "Billing Features"
Cohesion: 0.12
Nodes (9): SubscriptionManagement.Billing.Features.Renewals, SubscriptionManagement.Billing.Features.CreditNotes.ListCreditNotes, SubscriptionManagement.Billing.Features.CreditNotes.IssueCreditNote, SubscriptionManagement.Billing.Features.CreditNotes, SubscriptionManagement.Billing.Tests, SubscriptionManagement.Contracts, SubscriptionManagement.Billing.Infrastructure, ListCreditNotesEndpoint (+1 more)

### Community 30 - "Community 30"
Cohesion: 0.08
Nodes (16): SubscriptionManagement.Payments.Features.Payments.Charge, SubscriptionManagement.Payments.Features.Payments.GetPaymentEvents, SubscriptionManagement.Payments.Features.Payments.GetPayment, SubscriptionManagement.Payments.Infrastructure, SubscriptionManagement.Payments.Features.Payments, SubscriptionManagement.Payments.Features.Payments.Cancel, SubscriptionManagement.Payments.Features.Payments.RefundCanceledSubscription, CancelPaymentHandler (+8 more)

### Community 31 - "Community 31"
Cohesion: 0.06
Nodes (32): ingestion, metrics_generator, burst_size_bytes, max_traces_per_user, rate_limit_bytes, memory, processors, enabled (+24 more)

### Community 32 - "Community 32"
Cohesion: 0.06
Nodes (33): cmds, desc, cmds, desc, cmds, desc, cmds, desc (+25 more)

### Community 33 - "Community 33"
Cohesion: 0.20
Nodes (5): CancelRenewal, ChargePayment, PaymentSucceeded, SettleInvoice, RenewalSagaFlowTests

### Community 34 - "Community 34"
Cohesion: 0.06
Nodes (31): allowed_payload_size, echo_downstream, fault_tolerant, generator, header_name, hide_client_headers, limit_by, minute (+23 more)

### Community 35 - "Community 35"
Cohesion: 0.09
Nodes (26): options, setup(), setup(), env(), envNumber(), json(), seconds(), seedSubscriptions() (+18 more)

### Community 36 - "Community 36"
Cohesion: 0.09
Nodes (9): ApiFixture, Billing, Client, Factory, PostgresConnectionString, Redis, RedisConnectionString, OutboxRow (+1 more)

### Community 37 - "Community 37"
Cohesion: 0.07
Nodes (29): apiVersion, prune, selfHeal, duration, factor, maxDuration, namespace, server (+21 more)

### Community 38 - "Community 38"
Cohesion: 0.07
Nodes (29): controller, ingressController, resources, headers, log_level, tracing_instrumentations, tracing_sampling_rate, gateway (+21 more)

### Community 39 - "Community 39"
Cohesion: 0.15
Nodes (13): GetInvoiceRenewalEndpoint, GetInvoiceRenewalHandler, GetInvoiceRenewalQuery, RenewalStatusResponse, IBillingGateway, RenewalQuote, RenewalStatus, UnconfiguredBillingGateway (+5 more)

### Community 40 - "Community 40"
Cohesion: 0.07
Nodes (28): alloy, configMap, enableReporting, extraEnv, extraPorts, mounts, resources, stabilityLevel (+20 more)

### Community 41 - "Community 41"
Cohesion: 0.11
Nodes (14): SearchCursor, SearchSubscriptionsEndpoint, SearchSubscriptionsQuery, SearchSubscriptionsResult, SubscriptionProblems, ApiProblemException, Detail, Errors (+6 more)

### Community 42 - "Community 42"
Cohesion: 0.16
Nodes (4): SubscriptionManagement.SystemTests, SubscriptionManagement.IntegrationTests, SubscriptionManagement.Infrastructure.Billing, Eventually

### Community 43 - "Community 43"
Cohesion: 0.08
Nodes (25): activation_command, activation_command_timeout, added_modes, agent_interface, default_modes, encoding, excluded_apis, excluded_tools (+17 more)

### Community 44 - "Community 44"
Cohesion: 0.08
Nodes (24): affinity, containerPort, externalRedis, connectionString, extraEnv, fullnameOverride, imagePullSecrets, nameOverride (+16 more)

### Community 45 - "Community 45"
Cohesion: 0.11
Nodes (24): datasource, datasource, gnetId, revision, gnetId, revision, aspnetcore, aspnetcore-endpoint (+16 more)

### Community 46 - "Community 46"
Cohesion: 0.15
Nodes (23): ADR-005 JIT messaging services beside the AOT API, SubscriptionManagement.Contracts, Explicit queue declaration for hidden outgoing types, ADR-006 Raw SQL outbox and JIT Relay, Raw SQL outbox (outbox_messages), Relay HTTP commands (settle, void, pause), Settlement and cancellation share subscription lock, FOR UPDATE SKIP LOCKED outbox drain (+15 more)

### Community 47 - "Community 47"
Cohesion: 0.18
Nodes (10): RenewalSaga, Amount, Currency, FailureReason, Id, PaymentId, State, SubscriptionId (+2 more)

### Community 48 - "Community 48"
Cohesion: 0.11
Nodes (22): duration, errors(), flow(), RFC-9457, options, reads(), renewalTimeout, required (+14 more)

### Community 49 - "Community 49"
Cohesion: 0.10
Nodes (21): controller, metrics, replicas, resources, dashboard, enabled, ingress, resources (+13 more)

### Community 50 - "Community 50"
Cohesion: 0.14
Nodes (20): ADR-001 Native AOT with Minimal APIs, AOT/trim analyzers as build errors, AppJsonSerializerContext source-generated JSON, Minimal APIs with CreateSlimBuilder, ADR-002 Npgsql plus explicit SQL instead of EF Core, Per-aggregate Npgsql stores with explicit SQL, Not done and follow-ups, Implementation plan (living document) (+12 more)

### Community 51 - "Community 51"
Cohesion: 0.15
Nodes (21): invoiceFlow(), lifecycleFlow(), uuid(), expectProblem(), expectStatus(), fullLifecycle(), mixedWorkload(), subscriptionBody() (+13 more)

### Community 52 - "Community 52"
Cohesion: 0.10
Nodes (19): backend, replicas, chunksCache, enabled, deploymentMode, gateway, enabled, lokiCanary (+11 more)

### Community 53 - "Community 53"
Cohesion: 0.12
Nodes (19): ASPIRE_ALLOW_UNSECURED_TRANSPORT, ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL, ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL, ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages (+11 more)

### Community 54 - "Community 54"
Cohesion: 0.18
Nodes (19): image, digest, pullPolicy, repository, tag, enabled, image, resources (+11 more)

### Community 56 - "Community 56"
Cohesion: 0.12
Nodes (9): BillingDbContext, CreditNotes, RenewalSagas, RelayDbContext, BillingFixture, App, Client, PostgresConnectionString (+1 more)

### Community 57 - "Community 57"
Cohesion: 0.29
Nodes (4): RenewalTimeout, InvoiceIssued, FixedTime, RenewalSagaTests

### Community 58 - "Community 58"
Cohesion: 0.11
Nodes (17): defaultRules, rules, fullnameOverride, kubeControllerManager, enabled, kubeEtcd, enabled, kubeProxy (+9 more)

### Community 59 - "Community 59"
Cohesion: 0.13
Nodes (11): api, BASE, BILLING, RFC-9457, JSON_HEADERS, lifecycleDuration, PAYMENTS, trackCalls() (+3 more)

### Community 60 - "Community 60"
Cohesion: 0.11
Nodes (17): global, evaluation_interval, scrape_interval, replica, otlp, keep_identifying_resource_attributes, family, scrape_configs (+9 more)

### Community 62 - "Community 62"
Cohesion: 0.13
Nodes (12): CreditNote, Amount, Currency, Id, InvoiceId, IssuedAt, PaymentId, Reason (+4 more)

### Community 63 - "Community 63"
Cohesion: 0.21
Nodes (4): CreditNoteList, CreditNoteResponse, ServiceSetup, CreditNoteTests

### Community 64 - "Community 64"
Cohesion: 0.12
Nodes (17): enabled, failureLimit, initialDelay, interval, maxErrorRatio, maxP95Seconds, prometheusAddress, maxSurge (+9 more)

### Community 65 - "Community 65"
Cohesion: 0.15
Nodes (17): path, port, failureThreshold, httpGet, periodSeconds, timeoutSeconds, probes, liveness (+9 more)

### Community 66 - "Community 66"
Cohesion: 0.12
Nodes (17): jsonData, datasourceUid, matcherRegex, name, url, urlDisplayLabel, derivedFields, lokiSearch (+9 more)

### Community 67 - "Community 67"
Cohesion: 0.12
Nodes (17): prometheusSpec, enableFeatures, enableRemoteWriteReceiver, evaluationInterval, podMonitorSelectorNilUsesHelmValues, probeSelectorNilUsesHelmValues, retention, retentionSize (+9 more)

### Community 68 - "Community 68"
Cohesion: 0.17
Nodes (16): alertmanager, alertmanagerSpec, resources, resources, kube-state-metrics, resources, memory, prometheus-node-exporter (+8 more)

### Community 69 - "Community 69"
Cohesion: 0.12
Nodes (16): cmds, desc, dotenv, env, cmds, desc, dotenv, env (+8 more)

### Community 70 - "Community 70"
Cohesion: 0.13
Nodes (14): runs-on, steps, jobs, build-test-aot, name, on, pull_request, push (+6 more)

### Community 71 - "Community 71"
Cohesion: 0.14
Nodes (13): apiVersion, kind, argocd.argoproj.io/secret-type, metadata, labels, name, namespace, stringData (+5 more)

### Community 72 - "Community 72"
Cohesion: 0.15
Nodes (6): load_env(), env.sh script, k6-test.sh script, load-test.sh script, locust-test.sh script, stress-test.sh script

### Community 73 - "Community 73"
Cohesion: 0.21
Nodes (11): decline(), flows, options, rejected(), renew(), settleSeconds, success(), timeout() (+3 more)

### Community 74 - "Community 74"
Cohesion: 0.29
Nodes (13): abort(), failures, iterations, options, paymentEvents(), poll(), readJson(), renewal() (+5 more)

### Community 76 - "Community 76"
Cohesion: 0.15
Nodes (13): database, existingSecret, password, username, enabled, size, storageClass, postgresql (+5 more)

### Community 77 - "Community 77"
Cohesion: 0.15
Nodes (13): reporting_enabled, replication_factor, delete_request_store, retention_enabled, loki, analytics, auth_enabled, commonConfig (+5 more)

### Community 79 - "Community 79"
Cohesion: 0.17
Nodes (12): enabled, folder, metrics, grafanaDashboard, monitoringNamespace, prometheusRule, serviceMonitor, enabled (+4 more)

### Community 80 - "Community 80"
Cohesion: 0.23
Nodes (12): ADR-004 Hand-written validation and RFC 9457 pipeline, ApiProblemException and IExceptionHandler pipeline, Correlation middleware (X-Trace-Id), Static validator methods, Deliberate behaviour decisions, Implementation deviations from Java, Java reference vs .NET feature gap analysis, Java Spring Boot reference implementation (+4 more)

### Community 81 - "Community 81"
Cohesion: 0.23
Nodes (10): Keyset pagination search, Native AOT strategy, System architecture, ILCompiler 10.0.14 pin conflict, API and service boundaries, Technical design, Subscription API (Native AOT), Docker and compose packaging (+2 more)

### Community 82 - "Community 82"
Cohesion: 0.26
Nodes (12): Observability design, Saga state transition table, Testing and deployment, Aspire AppHost, Monitoring stack (OTel Collector, Prometheus, Tempo, Loki, Grafana), k6 load-test scenarios, Renewal saga workflow, SonarQube code quality (+4 more)

### Community 83 - "Community 83"
Cohesion: 0.17
Nodes (10): Aspire.Hosting.PostgreSQL (13.6.5), Aspire.Hosting.RabbitMQ (13.6.5), Aspire.Hosting.Redis (13.6.5), CommunityToolkit.Aspire.Hosting.k6 (13.6.1), CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector (13.6.1), dbup-postgresql (7.0.1), Microsoft.Extensions.Logging.Console (10.0.12), Aspire.AppHost.Sdk/13.6.5 (+2 more)

### Community 84 - "Community 84"
Cohesion: 0.21
Nodes (8): body(), createSubscription(), fullLifecycle(), JSON_HDR, lifecycle, options, setup(), vus

### Community 85 - "Community 85"
Cohesion: 0.17
Nodes (7): RenewalState, AwaitingPayment, Compensated, Completed, Declined, Settling, TimedOut

### Community 87 - "Community 87"
Cohesion: 0.24
Nodes (11): memory, migrator, enabled, resources, resources, resources, cpu, memory (+3 more)

### Community 88 - "Community 88"
Cohesion: 0.18
Nodes (10): apiVersion, containerdConfigPatches, containerPort, hostPort, listenAddress, protocol, kind, nodes (+2 more)

### Community 89 - "Community 89"
Cohesion: 0.18
Nodes (11): grafana, adminPassword, adminUser, fullnameOverride, ingress, persistence, enabled, hosts (+3 more)

### Community 90 - "Community 90"
Cohesion: 0.18
Nodes (11): memory, enabled, size, cpu, memory, limits, requests, singleBinary (+3 more)

### Community 91 - "Community 91"
Cohesion: 0.33
Nodes (10): exactlyOne(), options, parallel, planRace(), rowRaces(), violations, catalog(), planBody() (+2 more)

### Community 92 - "Community 92"
Cohesion: 0.18
Nodes (10): apiVersion, path, providers, allowUiUpdates, disableDeletion, folder, name, options (+2 more)

### Community 93 - "Community 93"
Cohesion: 0.22
Nodes (11): exporters, otlp/tempo, otlphttp/loki, otlphttp/prometheus, endpoint, tls, endpoint, tls (+3 more)

### Community 94 - "Community 94"
Cohesion: 0.18
Nodes (10): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, profiles (+2 more)

### Community 95 - "Community 95"
Cohesion: 0.22
Nodes (3): SubscriptionManagement.Relay.Tests, ContractRoutingTests, HostSmokeTests

### Community 96 - "Community 96"
Cohesion: 0.20
Nodes (10): host, paths, ingress, annotations, className, enabled, hosts, tls (+2 more)

### Community 97 - "Community 97"
Cohesion: 0.20
Nodes (10): dashboardproviders.yaml, apiVersion, providers, dashboardProviders, path, disableDeletion, folder, name (+2 more)

### Community 98 - "Community 98"
Cohesion: 0.20
Nodes (10): folderAnnotation, provider, exemplarTraceIdDestinations, datasourceUid, traceIdLabelName, urlDisplayLabel, sidecar, foldersFromFilesStructure (+2 more)

### Community 99 - "Community 99"
Cohesion: 0.22
Nodes (7): Marten (9.59.0), WolverineFx.Marten (6.64.0), Microsoft.NET.Sdk, WolverineFx (6.64.0), WolverineFx.RabbitMQ (6.64.0), WolverineFx.RuntimeCompilation (6.64.0), Microsoft.NET.Sdk.Web

### Community 100 - "Community 100"
Cohesion: 0.20
Nodes (9): Testcontainers.Redis (4.15.0), net10.0, coverlet.collector (6.0.4), Microsoft.AspNetCore.Mvc.Testing (10.0.12), Microsoft.NET.Test.Sdk (17.14.1), Testcontainers.PostgreSql (4.15.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4) (+1 more)

### Community 101 - "Community 101"
Cohesion: 0.20
Nodes (9): send_batch_max_size, send_batch_size, timeout, check_interval, limit_percentage, spike_limit_percentage, processors, batch (+1 more)

### Community 102 - "Community 102"
Cohesion: 0.20
Nodes (10): exporters, processors, receivers, exporters, processors, receivers, logs, metrics (+2 more)

### Community 103 - "Community 103"
Cohesion: 0.20
Nodes (9): block_retention, compactor, compaction, ingester, max_block_duration, server, http_listen_port, log_level (+1 more)

### Community 104 - "Community 104"
Cohesion: 0.22
Nodes (8): apiVersion, appVersion, description, keywords, kubeVersion, name, type, version

### Community 105 - "Community 105"
Cohesion: 0.22
Nodes (9): from, index, object_store, schema, store, period, prefix, schemaConfig (+1 more)

### Community 106 - "Community 106"
Cohesion: 0.22
Nodes (8): Google.Protobuf (3.36.2), Grpc.Net.ClientFactory (2.85.0), Grpc.Tools (2.85.0), Microsoft.AspNetCore.OpenApi (10.0.12), Microsoft.Extensions.Caching.StackExchangeRedis (10.0.12), Scalar.AspNetCore (2.17.11), Npgsql (10.0.3), Microsoft.NET.Sdk.Web

### Community 107 - "Community 107"
Cohesion: 0.22
Nodes (8): Grpc.AspNetCore (2.85.0), Npgsql.EntityFrameworkCore.PostgreSQL (10.0.3), WolverineFx (6.64.0), WolverineFx.EntityFrameworkCore (6.64.0), WolverineFx.Postgresql (6.64.0), WolverineFx.RabbitMQ (6.64.0), WolverineFx.RuntimeCompilation (6.64.0), Microsoft.NET.Sdk.Web

### Community 108 - "Community 108"
Cohesion: 0.22
Nodes (9): ingestion, metrics_generator, burst_size_bytes, max_traces_per_user, rate_limit_bytes, processors, trace_id_label_name, overrides (+1 more)

### Community 109 - "Community 109"
Cohesion: 0.22
Nodes (9): distributor, receivers, endpoint, max_recv_msg_size_mib, endpoint, protocols, grpc, http (+1 more)

### Community 110 - "Community 110"
Cohesion: 0.22
Nodes (8): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, profiles, http, $schema

### Community 111 - "Community 111"
Cohesion: 0.22
Nodes (8): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, profiles, http, $schema

### Community 112 - "Community 112"
Cohesion: 0.22
Nodes (8): net10.0, coverlet.collector (6.0.4), Microsoft.AspNetCore.TestHost (10.0.12), Microsoft.NET.Test.Sdk (17.14.1), Testcontainers.PostgreSql (4.15.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), Microsoft.NET.Sdk

### Community 113 - "Community 113"
Cohesion: 0.22
Nodes (8): net10.0, coverlet.collector (6.0.4), Microsoft.AspNetCore.TestHost (10.0.12), Microsoft.NET.Test.Sdk (17.14.1), Testcontainers.PostgreSql (4.15.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), Microsoft.NET.Sdk

### Community 114 - "Community 114"
Cohesion: 0.22
Nodes (8): net10.0, coverlet.collector (6.0.4), Microsoft.AspNetCore.TestHost (10.0.12), Microsoft.NET.Test.Sdk (17.14.1), Testcontainers.PostgreSql (4.15.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), Microsoft.NET.Sdk

### Community 115 - "Community 115"
Cohesion: 0.22
Nodes (8): net10.0, coverlet.collector (6.0.4), Microsoft.AspNetCore.Mvc.Testing (10.0.12), Microsoft.NET.Test.Sdk (17.14.1), Testcontainers.PostgreSql (4.15.0), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), Microsoft.NET.Sdk

### Community 117 - "Community 117"
Cohesion: 0.25
Nodes (8): config, cacheSubscriptionTtl, environment, logLevel, openApiEnabled, otlpEndpoint, otlpMetricExportIntervalMs, postgresMaxPoolSize

### Community 118 - "Community 118"
Cohesion: 0.25
Nodes (8): check_for_updates, reporting_enabled, grafana.ini, analytics, plugins, server, disable_plugins, root_url

### Community 119 - "Community 119"
Cohesion: 0.25
Nodes (7): apiVersion, data, localRegistryHosting.v1, kind, metadata, name, namespace

### Community 120 - "Community 120"
Cohesion: 0.25
Nodes (7): ASPNETCORE_ENVIRONMENT, commandName, dotnetRunMessages, environmentVariables, profiles, http, $schema

### Community 121 - "Community 121"
Cohesion: 0.25
Nodes (7): Npgsql.EntityFrameworkCore.PostgreSQL (10.0.3), WolverineFx (6.64.0), WolverineFx.EntityFrameworkCore (6.64.0), WolverineFx.Postgresql (6.64.0), WolverineFx.RabbitMQ (6.64.0), WolverineFx.RuntimeCompilation (6.64.0), Microsoft.NET.Sdk.Web

### Community 122 - "Community 122"
Cohesion: 0.52
Nodes (7): Helm release NOTES.txt, Argo CD and Argo Rollouts GitOps, Canary release with Prometheus AnalysisTemplate, Helm chart subscription-management, Local Kubernetes platform (kind), Kong gateway, Local image registry (kind-registry)

### Community 123 - "Community 123"
Cohesion: 0.29
Nodes (7): externalDatabase, database, existingSecret, host, password, port, username

### Community 124 - "Community 124"
Cohesion: 0.29
Nodes (7): podSecurityContext, fsGroup, runAsGroup, runAsNonRoot, runAsUser, seccompProfile, type

### Community 125 - "Community 125"
Cohesion: 0.29
Nodes (6): Npgsql.OpenTelemetry (10.0.3), OpenTelemetry.Exporter.OpenTelemetryProtocol (1.19.1), OpenTelemetry.Extensions.Hosting (1.19.1), OpenTelemetry.Instrumentation.AspNetCore (1.19.0), OpenTelemetry.Instrumentation.Http (1.19.0), Microsoft.NET.Sdk

### Community 126 - "Community 126"
Cohesion: 0.48
Nodes (4): check(), fail(), json(), aot-smoke.sh script

### Community 127 - "Community 127"
Cohesion: 0.57
Nodes (6): api(), init(), sonar.sh script, report(), wait_for_task(), wait_up()

### Community 128 - "Community 128"
Cohesion: 0.29
Nodes (7): endpoint, endpoint, protocols, grpc, http, receivers, otlp

### Community 129 - "Community 129"
Cohesion: 0.29
Nodes (7): level, readers, exporter, pull, telemetry, logs, metrics

### Community 130 - "Community 130"
Cohesion: 0.29
Nodes (7): source, metrics_generator, registry, traces_storage, collection_interval, external_labels, path

### Community 131 - "Community 131"
Cohesion: 0.29
Nodes (7): path, storage, trace, backend, local, wal, path

### Community 132 - "Community 132"
Cohesion: 0.29
Nodes (7): processor, service_graphs, span_metrics, enable_messaging_system_latency_histogram, histogram_buckets, max_items, dimensions

### Community 133 - "Community 133"
Cohesion: 0.29
Nodes (7): cmds, deps, desc, preconditions, msg, sh, app:deploy

### Community 134 - "Community 134"
Cohesion: 0.33
Nodes (7): cmd, for, cmds, desc, silent, cmds, kong:routes

### Community 135 - "Community 135"
Cohesion: 0.29
Nodes (7): CLUSTER, REGISTRY_NAME, REGISTRY_PORT, cmds, desc, env, registry:setup

### Community 136 - "Community 136"
Cohesion: 0.29
Nodes (6): net10.0, coverlet.collector (6.0.4), Microsoft.NET.Test.Sdk (17.14.1), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), Microsoft.NET.Sdk

### Community 137 - "Community 137"
Cohesion: 0.40
Nodes (4): SubscriptionManagement.Features.Invoices.ListInvoicesBySubscription, ListInvoicesBySubscriptionEndpoint, ListInvoicesBySubscriptionHandler, ListInvoicesBySubscriptionQuery

### Community 138 - "Community 138"
Cohesion: 0.33
Nodes (6): autoscaling, enabled, maxReplicas, minReplicas, targetCPUUtilizationPercentage, targetMemoryUtilizationPercentage

### Community 139 - "Community 139"
Cohesion: 0.33
Nodes (6): access, name, type, uid, url, additionalDataSources

### Community 140 - "Community 140"
Cohesion: 0.33
Nodes (6): tracesToLogsV2, datasourceUid, filterByTraceID, spanEndTimeShift, spanStartTimeShift, tags

### Community 141 - "Community 141"
Cohesion: 0.40
Nodes (6): ADR-003 Versioned SQL migrations in a dedicated executable, SubscriptionManagement.Migrator executable, schema_migrations history with checksum and advisory lock, Capability matrix, DbUp-based SQL migrator, PostgreSQL primary, replicas and PgBouncer

### Community 142 - "Community 142"
Cohesion: 0.33
Nodes (6): otlp/aspire, x-otlp-api-key, endpoint, headers, tls, insecure_skip_verify

### Community 143 - "Community 143"
Cohesion: 0.33
Nodes (6): cmds, desc, sources, status, exclude, image:build

### Community 144 - "Community 144"
Cohesion: 0.33
Nodes (6): cmds, desc, prompt, vars, registry:delete, VOLUME

### Community 145 - "Community 145"
Cohesion: 0.40
Nodes (5): drop, securityContext, allowPrivilegeEscalation, capabilities, readOnlyRootFilesystem

### Community 146 - "Community 146"
Cohesion: 0.40
Nodes (5): serviceAccount, annotations, automount, create, name

### Community 147 - "Community 147"
Cohesion: 0.40
Nodes (5): storage, send_exemplars, url, path, remote_write

### Community 149 - "Community 149"
Cohesion: 0.50
Nodes (4): allow_structured_metadata, retention_period, volume_enabled, limits_config

### Community 150 - "Community 150"
Cohesion: 0.50
Nodes (4): traces, exporters, processors, receivers

### Community 151 - "Community 151"
Cohesion: 0.50
Nodes (4): cmds, desc, interactive, app:psql

### Community 152 - "Community 152"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, app:publish

### Community 153 - "Community 153"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, argocd:install

### Community 154 - "Community 154"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, argocd:open

### Community 155 - "Community 155"
Cohesion: 0.50
Nodes (4): cmds, desc, status, cluster:create

### Community 156 - "Community 156"
Cohesion: 0.50
Nodes (4): cmds, desc, prompt, cluster:delete

### Community 157 - "Community 157"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, cluster:ports

### Community 158 - "Community 158"
Cohesion: 0.50
Nodes (4): cmds, desc, status, gitops:adopt

### Community 159 - "Community 159"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, grafana:open

### Community 160 - "Community 160"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, image:push

### Community 161 - "Community 161"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, kong:install

### Community 162 - "Community 162"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, metrics:install

### Community 163 - "Community 163"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, monitoring:install

### Community 164 - "Community 164"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, monitoring:status

### Community 165 - "Community 165"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, registry:images

### Community 166 - "Community 166"
Cohesion: 0.50
Nodes (4): cmds, desc, interactive, rollout:watch

### Community 167 - "Community 167"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, rollouts:install

### Community 168 - "Community 168"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, smoke

### Community 169 - "Community 169"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, sonar:open

### Community 170 - "Community 170"
Cohesion: 0.50
Nodes (4): cmds, desc, prompt, sonar:purge

### Community 171 - "Community 171"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, sonar:scan

### Community 172 - "Community 172"
Cohesion: 0.50
Nodes (4): tools:check, cmds, desc, silent

### Community 173 - "Community 173"
Cohesion: 0.50
Nodes (4): tools:helm, cmds, desc, status

### Community 174 - "Community 174"
Cohesion: 0.50
Nodes (4): tools:rollouts-plugin, cmds, desc, status

### Community 175 - "Community 175"
Cohesion: 0.50
Nodes (4): trivy:image, cmds, deps, desc

### Community 176 - "Community 176"
Cohesion: 0.67
Nodes (3): enabled, sidecar, rules

### Community 177 - "Community 177"
Cohesion: 0.67
Nodes (3): cmds, desc, alertmanager:port-forward

### Community 178 - "Community 178"
Cohesion: 0.67
Nodes (3): cmds, desc, app:lint

### Community 179 - "Community 179"
Cohesion: 0.67
Nodes (3): cmds, desc, app:logs

### Community 180 - "Community 180"
Cohesion: 0.67
Nodes (3): cmds, desc, app:migrator-logs

### Community 181 - "Community 181"
Cohesion: 0.67
Nodes (3): cmds, desc, app:port-forward

### Community 182 - "Community 182"
Cohesion: 0.67
Nodes (3): cmds, desc, app:restart

### Community 183 - "Community 183"
Cohesion: 0.67
Nodes (3): cmds, desc, app:status

### Community 184 - "Community 184"
Cohesion: 0.67
Nodes (3): cmds, desc, argo:uninstall

### Community 185 - "Community 185"
Cohesion: 0.67
Nodes (3): cmds, desc, aspire:lean

### Community 186 - "Community 186"
Cohesion: 0.67
Nodes (3): cmds, desc, dev

### Community 187 - "Community 187"
Cohesion: 0.67
Nodes (3): cmds, desc, grafana:port-forward

### Community 188 - "Community 188"
Cohesion: 0.67
Nodes (3): cmds, desc, k6

### Community 189 - "Community 189"
Cohesion: 0.67
Nodes (3): cmds, desc, kong:uninstall

### Community 190 - "Community 190"
Cohesion: 0.67
Nodes (3): cmds, desc, load-test

### Community 191 - "Community 191"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:abort

### Community 192 - "Community 192"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:promote

### Community 193 - "Community 193"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:retry

### Community 194 - "Community 194"
Cohesion: 0.67
Nodes (3): cmds, desc, sonar:down

### Community 195 - "Community 195"
Cohesion: 0.67
Nodes (3): cmds, desc, sonar:report

### Community 196 - "Community 196"
Cohesion: 0.67
Nodes (3): cmds, desc, sonar:up

### Community 197 - "Community 197"
Cohesion: 0.67
Nodes (3): cmds, desc, stress-test

### Community 198 - "Community 198"
Cohesion: 0.67
Nodes (3): trivy:fs, cmds, desc

## Knowledge Gaps
- **218 isolated node(s):** `RFC-9457`, `RFC-9457`, `Aspire.Hosting.PostgreSQL (13.6.5)`, `Aspire.Hosting.Redis (13.6.5)`, `Aspire.Hosting.RabbitMQ (13.6.5)` (+213 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1272 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **11 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `tasks` connect `Community 32` to `Community 133`, `Community 134`, `Community 135`, `Community 143`, `Community 144`, `Taskfile Tasks`, `Locust Load Tests`, `Community 151`, `Community 152`, `Community 153`, `Community 154`, `Community 155`, `Community 156`, `Community 157`, `Community 158`, `Community 159`, `Community 160`, `Community 161`, `Community 162`, `Community 163`, `Community 164`, `Community 165`, `Community 166`, `Community 167`, `Community 168`, `Community 169`, `Community 170`, `Community 171`, `Community 172`, `Community 173`, `Community 174`, `Community 175`, `Community 177`, `Community 178`, `Community 179`, `Community 180`, `Community 181`, `Community 182`, `Community 183`, `Community 184`, `Community 185`, `Community 186`, `Community 187`, `Community 188`, `Community 189`, `Community 190`, `Community 191`, `Community 192`, `Community 193`, `Community 194`, `Community 195`, `Community 196`, `Community 69`, `Community 197`, `Community 198`?**
  _High betweenness centrality (0.011) - this node is a cross-community bridge._
- **What connects `RFC-9457`, `RFC-9457`, `Aspire.Hosting.PostgreSQL (13.6.5)` to the rest of the system?**
  _218 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Payments Event Sourcing` be split into smaller, more focused modules?**
  _Cohesion score 0.05733482642777156 - nodes in this community are weakly interconnected._
- **Why does `BillingGrpcServiceTests` connect `Billing gRPC Contract` to `Community 56`, `Community 85`, `Billing Features`?**
  _High betweenness centrality (0.006) - this node is a cross-community bridge._
- **Should `AppHost & DbUp Migrations` be split into smaller, more focused modules?**
  _Cohesion score 0.050351721584598295 - nodes in this community are weakly interconnected._
- **Why does `Subscription` connect `Subscription Aggregate` to `Invoice Queries`, `Subscription Handlers`, `Community 39`?**
  _High betweenness centrality (0.006) - this node is a cross-community bridge._
- **Should `Host Setup & Telemetry` be split into smaller, more focused modules?**
  _Cohesion score 0.06351236146632566 - nodes in this community are weakly interconnected._