# Graph Report - VerticalSliceSubsDotNetAOT  (2026-11-05)

## Corpus Check
- Corpus is ~37,301 words - fits in a single context window. You may not need a graph.

## Summary
- 1684 nodes · 2740 edges · 125 communities (116 shown, 9 thin omitted)
- Extraction: 94% EXTRACTED · 6% INFERRED · 0% AMBIGUOUS · INFERRED: 175 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- community_0
- community_1
- community_2
- community_3
- community_4
- community_5
- community_6
- community_7
- community_8
- community_9
- community_10
- community_11
- community_12
- community_13
- community_14
- community_15
- community_16
- community_17
- community_18
- community_19
- community_20
- community_21
- community_22
- community_23
- community_24
- community_25
- community_26
- community_27
- community_28
- community_29
- community_30
- community_31
- community_32
- community_33
- community_34
- community_35
- community_36
- community_37
- community_38
- community_39
- community_40
- community_41
- community_42
- community_43
- community_44
- community_45
- community_46
- community_47
- community_48
- community_49
- community_50
- community_51
- community_52
- community_53
- community_54
- community_55
- community_56
- community_57
- community_58
- community_59
- community_60
- community_61
- community_62
- community_63
- community_64
- community_65
- community_66
- community_67
- community_68
- community_69
- community_70
- community_71
- community_72
- community_73
- community_74
- community_75
- community_76
- community_77
- community_78
- community_79
- community_80
- community_81
- community_82
- community_83
- community_84
- community_85
- community_86
- community_87
- community_88
- community_89
- community_90
- community_91
- community_92
- community_93
- community_94
- community_95
- community_96
- community_97
- community_98
- community_99
- community_100
- community_101
- community_102
- community_103
- community_104
- community_105
- community_106
- community_107
- community_108
- community_109
- community_110
- community_111
- community_112
- community_113
- community_114
- community_115
- community_116
- community_117
- community_118
- community_119
- community_120
- community_121
- community_122
- community_123
- community_124

## God Nodes (most connected - your core abstractions)
1. `tasks` - 59 edges
2. `vars` - 35 edges
3. `SubscriptionManagement.Infrastructure` - 35 edges
4. `ApiProblemException` - 32 edges
5. `Subscription` - 28 edges
6. `Invoice` - 26 edges
7. `ApiFixture` - 26 edges
8. `ApiTestBase` - 17 edges
9. `SubscriptionStatus` - 16 edges
10. `ValidatorTests` - 16 edges

## Surprising Connections (you probably didn't know these)
- `README` --references--> `Helm NOTES.txt`  [INFERRED]
  README.md → deploy/helm/subscription-management/templates/NOTES.txt
- `InvoiceTests` --references--> `BilledPeriod`  [EXTRACTED]
  tests/SubscriptionManagement.UnitTests/InvoiceTests.cs → src/SubscriptionManagement/Features/Subscriptions/Subscription.cs
- `README` --references--> `ADR-001: Native AOT`  [INFERRED]
  README.md → docs/adr/001-native-aot.md
- `README` --references--> `ADR-002: Npgsql + SQL`  [INFERRED]
  README.md → docs/adr/002-data-access-npgsql.md
- `README` --references--> `.NET 10 Compatibility`  [INFERRED]
  README.md → docs/migration/dotnet10-compatibility.md

## Import Cycles
- None detected.

## Communities (125 total, 9 thin omitted)

### Community 0 - "community_0"
Cohesion: 0.08
Nodes (17): Program, ApiCollection, ApiFixture, Client, Factory, PostgresConnectionString, Redis, RedisConnectionString (+9 more)

### Community 1 - "community_1"
Cohesion: 0.06
Nodes (29): GetInvoiceQuery, Invoice, Amount, Currency, Id, IssuedAt, PaidAt, PeriodEnd (+21 more)

### Community 2 - "community_2"
Cohesion: 0.06
Nodes (21): CreatePlanCommand, CreateSubscriptionCommand, BilledPeriod, Subscription, CurrentPeriodEnd, CurrentPeriodStart, Id, Price (+13 more)

### Community 3 - "community_3"
Cohesion: 0.05
Nodes (18): SearchCursor, SearchSubscriptionsHandler, SearchSubscriptionsQuery, SearchSubscriptionsResult, SubscriptionProblems, ApiExceptionHandler, ApiProblemException, Detail (+10 more)

### Community 4 - "community_4"
Cohesion: 0.05
Nodes (48): kubernetes.io/ingress.class, bandwidth_metrics, latency_metrics, propagation, resource_attributes, status_code_metrics, traces_endpoint, upstream_health_metrics (+40 more)

### Community 5 - "community_5"
Cohesion: 0.05
Nodes (47): build, depends_on, environment, healthcheck, image, ports, restart, context (+39 more)

### Community 6 - "community_6"
Cohesion: 0.05
Nodes (35): body(), StepShape, SubscriptionUser, cmds, deps, desc, preconditions, cmds (+27 more)

### Community 7 - "community_7"
Cohesion: 0.05
Nodes (41): autoscaling, enabled, maxReplicas, minReplicas, config, environment, minute, openApiEnabled (+33 more)

### Community 8 - "community_8"
Cohesion: 0.06
Nodes (40): applicationSet, replicas, exec.enabled, timeout.reconciliation, timeout.reconciliation.jitter, configs, cm, params (+32 more)

### Community 9 - "community_9"
Cohesion: 0.05
Nodes (38): sh, dotenv, vars, ALLOY_CHART_VERSION, API_HOST, API_IMAGE, API_REPO, APP_NAME (+30 more)

### Community 10 - "community_10"
Cohesion: 0.06
Nodes (32): ingestion, metrics_generator, burst_size_bytes, max_traces_per_user, rate_limit_bytes, memory, processors, enabled (+24 more)

### Community 11 - "community_11"
Cohesion: 0.07
Nodes (27): Microsoft.AspNetCore.Mvc.Testing (10.0.12), Microsoft.AspNetCore.OpenApi (10.0.12), Microsoft.Extensions.Caching.StackExchangeRedis (10.0.12), Microsoft.Extensions.Logging.Console (10.0.12), Npgsql.OpenTelemetry (10.0.3), OpenTelemetry.Exporter.OpenTelemetryProtocol (1.19.1), OpenTelemetry.Extensions.Hosting (1.19.1), OpenTelemetry.Instrumentation.AspNetCore (1.19.0) (+19 more)

### Community 12 - "community_12"
Cohesion: 0.06
Nodes (31): allowed_payload_size, echo_downstream, fault_tolerant, generator, header_name, hide_client_headers, limit_by, minute (+23 more)

### Community 13 - "community_13"
Cohesion: 0.07
Nodes (29): apiVersion, prune, selfHeal, duration, factor, maxDuration, namespace, server (+21 more)

### Community 14 - "community_14"
Cohesion: 0.07
Nodes (29): controller, ingressController, resources, headers, log_level, tracing_instrumentations, tracing_sampling_rate, gateway (+21 more)

### Community 15 - "community_15"
Cohesion: 0.07
Nodes (28): alloy, configMap, enableReporting, extraEnv, extraPorts, mounts, resources, stabilityLevel (+20 more)

### Community 16 - "community_16"
Cohesion: 0.12
Nodes (14): SubscriptionManagement.Features.Plans, SubscriptionManagement.Features.Plans.GetPlan, GetPlanEndpoint, GetPlanHandler, GetPlanQuery, Plan, PlanInterval, Month (+6 more)

### Community 17 - "community_17"
Cohesion: 0.12
Nodes (11): SubscriptionManagement.Features.Subscriptions, SubscriptionManagement.Features.Subscriptions.CreateSubscriptions, SubscriptionManagement.Infrastructure, SubscriptionManagement.UnitTests, SubscriptionManagement.Features.Subscriptions.RenewSubscription, SubscriptionManagement.Features.Invoices, SubscriptionManagement.Features.Plans.CreatePlan, CreateSubscriptionHandler (+3 more)

### Community 18 - "community_18"
Cohesion: 0.08
Nodes (25): activation_command, activation_command_timeout, added_modes, agent_interface, default_modes, encoding, excluded_apis, excluded_tools (+17 more)

### Community 20 - "community_20"
Cohesion: 0.11
Nodes (24): datasource, datasource, gnetId, revision, gnetId, revision, aspnetcore, aspnetcore-endpoint (+16 more)

### Community 21 - "community_21"
Cohesion: 0.09
Nodes (23): jsonData, datasourceUid, matcherRegex, name, url, urlDisplayLabel, derivedFields, lokiSearch (+15 more)

### Community 22 - "community_22"
Cohesion: 0.10
Nodes (21): controller, metrics, replicas, resources, dashboard, enabled, ingress, resources (+13 more)

### Community 23 - "community_23"
Cohesion: 0.09
Nodes (21): affinity, containerPort, externalRedis, connectionString, extraEnv, fullnameOverride, imagePullSecrets, nameOverride (+13 more)

### Community 24 - "community_24"
Cohesion: 0.10
Nodes (19): backend, replicas, chunksCache, enabled, deploymentMode, gateway, enabled, lokiCanary (+11 more)

### Community 25 - "community_25"
Cohesion: 0.14
Nodes (19): memory, enabled, resources, migrator, enabled, resources, metrics, resources (+11 more)

### Community 26 - "community_26"
Cohesion: 0.12
Nodes (6): SubscriptionManagement.Features.Invoices.PayInvoice, SubscriptionManagement.Features.Invoices.GetInvoice, GetInvoiceEndpoint, GetInvoiceHandler, PayInvoiceEndpoint, PayInvoiceHandler

### Community 27 - "community_27"
Cohesion: 0.14
Nodes (6): SubscriptionManagement.Features.Subscriptions.GetSubscription, CreatePlanHandler, CreatePlanValidator, GetSubscriptionEndpoint, GetSubscriptionHandler, GetSubscriptionQuery

### Community 28 - "community_28"
Cohesion: 0.12
Nodes (17): enabled, failureLimit, initialDelay, interval, maxErrorRatio, maxP95Seconds, prometheusAddress, maxSurge (+9 more)

### Community 29 - "community_29"
Cohesion: 0.15
Nodes (17): path, port, failureThreshold, httpGet, periodSeconds, timeoutSeconds, probes, liveness (+9 more)

### Community 30 - "community_30"
Cohesion: 0.12
Nodes (17): prometheusSpec, enableFeatures, enableRemoteWriteReceiver, evaluationInterval, podMonitorSelectorNilUsesHelmValues, probeSelectorNilUsesHelmValues, retention, retentionSize (+9 more)

### Community 31 - "community_31"
Cohesion: 0.14
Nodes (10): body(), createSubscription(), fullLifecycle(), JSON_HDR, lifecycle, options, setup(), vus (+2 more)

### Community 32 - "community_32"
Cohesion: 0.17
Nodes (16): alertmanager, alertmanagerSpec, resources, resources, kube-state-metrics, resources, memory, prometheus-node-exporter (+8 more)

### Community 33 - "community_33"
Cohesion: 0.13
Nodes (14): runs-on, steps, jobs, build-test-aot, name, on, pull_request, push (+6 more)

### Community 34 - "community_34"
Cohesion: 0.13
Nodes (15): cmds, desc, cmds, desc, cmds, desc, cmds, desc (+7 more)

### Community 35 - "community_35"
Cohesion: 0.14
Nodes (13): apiVersion, kind, argocd.argoproj.io/secret-type, metadata, labels, name, namespace, stringData (+5 more)

### Community 36 - "community_36"
Cohesion: 0.15
Nodes (13): database, existingSecret, password, username, enabled, size, storageClass, postgresql (+5 more)

### Community 37 - "community_37"
Cohesion: 0.15
Nodes (13): reporting_enabled, replication_factor, delete_request_store, retention_enabled, loki, analytics, auth_enabled, commonConfig (+5 more)

### Community 38 - "community_38"
Cohesion: 0.17
Nodes (12): enabled, folder, metrics, grafanaDashboard, monitoringNamespace, prometheusRule, serviceMonitor, enabled (+4 more)

### Community 39 - "community_39"
Cohesion: 0.47
Nodes (12): README, ADR-001: Native AOT, ADR-002: Npgsql + SQL, ADR-003: SQL Migrations, ADR-004: Validation + Problem Details, .NET 10 Compatibility, Helm NOTES.txt, Implementation Plan (+4 more)

### Community 40 - "community_40"
Cohesion: 0.18
Nodes (10): apiVersion, containerdConfigPatches, containerPort, hostPort, listenAddress, protocol, kind, nodes (+2 more)

### Community 41 - "community_41"
Cohesion: 0.18
Nodes (10): fullnameOverride, kubeControllerManager, enabled, kubeEtcd, enabled, kubeProxy, enabled, kubeScheduler (+2 more)

### Community 42 - "community_42"
Cohesion: 0.18
Nodes (11): grafana, adminPassword, adminUser, fullnameOverride, ingress, persistence, enabled, hosts (+3 more)

### Community 43 - "community_43"
Cohesion: 0.18
Nodes (11): memory, enabled, size, cpu, memory, limits, requests, singleBinary (+3 more)

### Community 45 - "community_45"
Cohesion: 0.18
Nodes (4): CreatePlanEndpoint, CreateSubscriptionEndpoint, RenewSubscriptionEndpoint, RenewSubscriptionCommand

### Community 46 - "community_46"
Cohesion: 0.18
Nodes (10): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, profiles (+2 more)

### Community 47 - "community_47"
Cohesion: 0.20
Nodes (10): host, paths, ingress, annotations, className, enabled, hosts, tls (+2 more)

### Community 48 - "community_48"
Cohesion: 0.42
Nodes (10): image, digest, pullPolicy, repository, tag, image, image, image (+2 more)

### Community 49 - "community_49"
Cohesion: 0.20
Nodes (10): dashboardproviders.yaml, apiVersion, providers, dashboardProviders, path, disableDeletion, folder, name (+2 more)

### Community 50 - "community_50"
Cohesion: 0.20
Nodes (10): folderAnnotation, provider, exemplarTraceIdDestinations, datasourceUid, traceIdLabelName, urlDisplayLabel, sidecar, foldersFromFilesStructure (+2 more)

### Community 51 - "community_51"
Cohesion: 0.22
Nodes (5): load_env(), env.sh script, load-test.sh script, locust-test.sh script, stress-test.sh script

### Community 52 - "community_52"
Cohesion: 0.22
Nodes (8): apiVersion, appVersion, description, keywords, kubeVersion, name, type, version

### Community 53 - "community_53"
Cohesion: 0.22
Nodes (9): from, index, object_store, schema, store, period, prefix, schemaConfig (+1 more)

### Community 54 - "community_54"
Cohesion: 0.25
Nodes (8): config, cacheSubscriptionTtl, environment, logLevel, openApiEnabled, otlpEndpoint, otlpMetricExportIntervalMs, postgresMaxPoolSize

### Community 55 - "community_55"
Cohesion: 0.25
Nodes (8): check_for_updates, reporting_enabled, grafana.ini, analytics, plugins, server, disable_plugins, root_url

### Community 56 - "community_56"
Cohesion: 0.25
Nodes (7): apiVersion, data, localRegistryHosting.v1, kind, metadata, name, namespace

### Community 57 - "community_57"
Cohesion: 0.33
Nodes (4): SubscriptionManagement.Features.Subscriptions.ResumeSubscription, ResumeSubscriptionEndpoint, ResumeSubscriptionCommand, ResumeSubscriptionHandler

### Community 58 - "community_58"
Cohesion: 0.33
Nodes (4): SubscriptionManagement.Features.Subscriptions.CancelSubscriptions, CancelSubscriptionEndpoint, CancelSubscriptionCommand, CancelSubscriptionHandler

### Community 59 - "community_59"
Cohesion: 0.33
Nodes (4): SubscriptionManagement.Features.Subscriptions.PauseSubscription, PauseSubscriptionEndpoint, PauseSubscriptionCommand, PauseSubscriptionHandler

### Community 60 - "community_60"
Cohesion: 0.29
Nodes (7): externalDatabase, database, existingSecret, host, password, port, username

### Community 61 - "community_61"
Cohesion: 0.29
Nodes (7): podSecurityContext, fsGroup, runAsGroup, runAsNonRoot, runAsUser, seccompProfile, type

### Community 62 - "community_62"
Cohesion: 0.29
Nodes (7): defaultRules, rules, etcd, kubeControllerManager, kubeProxy, kubeSchedulerAlerting, kubeSchedulerRecording

### Community 63 - "community_63"
Cohesion: 0.48
Nodes (4): check(), fail(), json(), aot-smoke.sh script

### Community 64 - "community_64"
Cohesion: 0.33
Nodes (7): cmd, for, cmds, desc, silent, cmds, kong:routes

### Community 65 - "community_65"
Cohesion: 0.29
Nodes (7): CLUSTER, REGISTRY_NAME, REGISTRY_PORT, cmds, desc, env, registry:setup

### Community 66 - "community_66"
Cohesion: 0.40
Nodes (3): SubscriptionManagement.Features.Plans.ListPlans, ListPlansEndpoint, ListPlansHandler

### Community 68 - "community_68"
Cohesion: 0.40
Nodes (4): SubscriptionManagement.Features.Invoices.ListInvoicesBySubscription, ListInvoicesBySubscriptionEndpoint, ListInvoicesBySubscriptionHandler, ListInvoicesBySubscriptionQuery

### Community 69 - "community_69"
Cohesion: 0.33
Nodes (6): autoscaling, enabled, maxReplicas, minReplicas, targetCPUUtilizationPercentage, targetMemoryUtilizationPercentage

### Community 70 - "community_70"
Cohesion: 0.33
Nodes (6): access, name, type, uid, url, additionalDataSources

### Community 72 - "community_72"
Cohesion: 0.33
Nodes (6): cmds, desc, sources, status, exclude, image:build

### Community 73 - "community_73"
Cohesion: 0.33
Nodes (6): cmds, desc, prompt, vars, registry:delete, VOLUME

### Community 74 - "community_74"
Cohesion: 0.40
Nodes (5): drop, securityContext, allowPrivilegeEscalation, capabilities, readOnlyRootFilesystem

### Community 75 - "community_75"
Cohesion: 0.40
Nodes (5): serviceAccount, annotations, automount, create, name

### Community 76 - "community_76"
Cohesion: 0.50
Nodes (4): service, annotations, port, type

### Community 77 - "community_77"
Cohesion: 0.50
Nodes (4): allow_structured_metadata, retention_period, volume_enabled, limits_config

### Community 80 - "community_80"
Cohesion: 0.50
Nodes (4): cmds, desc, interactive, app:psql

### Community 81 - "community_81"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, app:publish

### Community 82 - "community_82"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, argocd:install

### Community 83 - "community_83"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, argocd:open

### Community 84 - "community_84"
Cohesion: 0.50
Nodes (4): cmds, desc, status, cluster:create

### Community 85 - "community_85"
Cohesion: 0.50
Nodes (4): cmds, desc, prompt, cluster:delete

### Community 86 - "community_86"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, cluster:ports

### Community 87 - "community_87"
Cohesion: 0.50
Nodes (4): cmds, desc, status, gitops:adopt

### Community 88 - "community_88"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, grafana:open

### Community 89 - "community_89"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, image:push

### Community 90 - "community_90"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, kong:install

### Community 91 - "community_91"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, metrics:install

### Community 92 - "community_92"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, monitoring:status

### Community 93 - "community_93"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, registry:images

### Community 94 - "community_94"
Cohesion: 0.50
Nodes (4): cmds, desc, interactive, rollout:watch

### Community 95 - "community_95"
Cohesion: 0.50
Nodes (4): cmds, deps, desc, rollouts:install

### Community 96 - "community_96"
Cohesion: 0.50
Nodes (4): cmds, desc, silent, smoke

### Community 97 - "community_97"
Cohesion: 0.50
Nodes (4): tools:check, cmds, desc, silent

### Community 98 - "community_98"
Cohesion: 0.50
Nodes (4): tools:helm, cmds, desc, status

### Community 99 - "community_99"
Cohesion: 0.50
Nodes (4): tools:rollouts-plugin, cmds, desc, status

### Community 100 - "community_100"
Cohesion: 0.67
Nodes (3): enabled, sidecar, rules

### Community 101 - "community_101"
Cohesion: 0.67
Nodes (3): cmds, desc, app:lint

### Community 102 - "community_102"
Cohesion: 0.67
Nodes (3): cmds, desc, app:logs

### Community 103 - "community_103"
Cohesion: 0.67
Nodes (3): cmds, desc, app:migrator-logs

### Community 104 - "community_104"
Cohesion: 0.67
Nodes (3): cmds, desc, app:port-forward

### Community 105 - "community_105"
Cohesion: 0.67
Nodes (3): cmds, desc, app:restart

### Community 106 - "community_106"
Cohesion: 0.67
Nodes (3): cmds, desc, app:status

### Community 107 - "community_107"
Cohesion: 0.67
Nodes (3): cmds, desc, app:template

### Community 108 - "community_108"
Cohesion: 0.67
Nodes (3): cmds, desc, app:test

### Community 109 - "community_109"
Cohesion: 0.67
Nodes (3): cmds, desc, app:uninstall

### Community 110 - "community_110"
Cohesion: 0.67
Nodes (3): cmds, desc, argo:uninstall

### Community 111 - "community_111"
Cohesion: 0.67
Nodes (3): cmds, silent, default

### Community 112 - "community_112"
Cohesion: 0.67
Nodes (3): cmds, desc, dev

### Community 113 - "community_113"
Cohesion: 0.67
Nodes (3): cmds, desc, kong:port-forward

### Community 114 - "community_114"
Cohesion: 0.67
Nodes (3): cmds, desc, kong:uninstall

### Community 115 - "community_115"
Cohesion: 0.67
Nodes (3): cmds, desc, load-test

### Community 116 - "community_116"
Cohesion: 0.67
Nodes (3): cmds, desc, prometheus:port-forward

### Community 117 - "community_117"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:abort

### Community 118 - "community_118"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:promote

### Community 119 - "community_119"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:retry

### Community 120 - "community_120"
Cohesion: 0.67
Nodes (3): cmds, desc, rollout:status

### Community 121 - "community_121"
Cohesion: 0.67
Nodes (3): cmds, desc, stress-test

## Knowledge Gaps
- **67 isolated node(s):** `Microsoft.Extensions.Logging.Console (10.0.12)`, `Npgsql (10.0.3)`, `Microsoft.NET.Sdk`, `Open`, `Paid` (+62 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 781 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `tasks` connect `community_34` to `community_6`, `community_9`, `community_64`, `community_65`, `community_72`, `community_73`, `community_80`, `community_81`, `community_82`, `community_83`, `community_84`, `community_85`, `community_86`, `community_87`, `community_88`, `community_89`, `community_90`, `community_91`, `community_92`, `community_93`, `community_94`, `community_95`, `community_96`, `community_97`, `community_98`, `community_99`, `community_101`, `community_102`, `community_103`, `community_104`, `community_105`, `community_106`, `community_107`, `community_108`, `community_109`, `community_110`, `community_111`, `community_112`, `community_113`, `community_114`, `community_115`, `community_116`, `community_117`, `community_118`, `community_119`, `community_120`, `community_121`?**
  _High betweenness centrality (0.032) - this node is a cross-community bridge._
- **What connects `Microsoft.Extensions.Logging.Console (10.0.12)`, `Npgsql (10.0.3)`, `Microsoft.NET.Sdk` to the rest of the system?**
  _67 weakly-connected nodes found - possible missing edges._
- **Should `community_0` be split into smaller, more focused modules?**
  _Cohesion score 0.07538659793814433 - nodes in this community are weakly interconnected._
- **Why does `SubscriptionManagement.Infrastructure` connect `community_17` to `community_1`, `community_66`, `community_3`, `community_68`, `community_58`, `community_2`, `community_71`, `community_59`, `community_78`, `community_79`, `community_16`, `community_19`, `community_57`, `community_26`, `community_27`?**
  _High betweenness centrality (0.013) - this node is a cross-community bridge._
- **Should `community_1` be split into smaller, more focused modules?**
  _Cohesion score 0.05811027224891329 - nodes in this community are weakly interconnected._
- **Why does `Subscription` connect `community_2` to `community_1`?**
  _High betweenness centrality (0.010) - this node is a cross-community bridge._
- **Should `community_2` be split into smaller, more focused modules?**
  _Cohesion score 0.06210526315789474 - nodes in this community are weakly interconnected._