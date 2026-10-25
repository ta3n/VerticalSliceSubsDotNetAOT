{{/* Chart name, truncated to the 63-char DNS label limit. */}}
{{- define "subs.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Fully qualified app name: <release>-<chart> unless the release name already contains the chart name. */}}
{{- define "subs.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "subs.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Labels shared by every object; pass a dict with "ctx" (root) and "component". */}}
{{- define "subs.labels" -}}
helm.sh/chart: {{ include "subs.chart" .ctx }}
{{ include "subs.selectorLabels" . }}
app.kubernetes.io/version: {{ .ctx.Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .ctx.Release.Service }}
app.kubernetes.io/part-of: subscription-management
{{- end }}

{{- define "subs.selectorLabels" -}}
app.kubernetes.io/name: {{ include "subs.name" .ctx }}
app.kubernetes.io/instance: {{ .ctx.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{- define "subs.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "subs.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/* repository:tag, plus @digest when one is given (the digest then decides what is pulled). */}}
{{- define "subs.image" -}}
{{- $ref := printf "%s:%s" .Values.image.repository (default .Chart.AppVersion .Values.image.tag) }}
{{- with .Values.image.digest }}{{ $ref = printf "%s@%s" $ref . }}{{ end }}
{{- $ref }}
{{- end }}

{{- define "subs.migratorImage" -}}
{{- $ref := printf "%s:%s" .Values.migrator.image.repository (default (default .Chart.AppVersion .Values.image.tag) .Values.migrator.image.tag) }}
{{- with .Values.migrator.image.digest }}{{ $ref = printf "%s@%s" $ref . }}{{ end }}
{{- $ref }}
{{- end }}

{{- define "subs.postgresql.fullname" -}}
{{- printf "%s-postgresql" (include "subs.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "subs.redis.fullname" -}}
{{- printf "%s-redis" (include "subs.fullname" .) | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* ---- Database connection ---- */}}

{{- define "subs.db.host" -}}
{{- if .Values.postgresql.enabled }}
{{- include "subs.postgresql.fullname" . }}
{{- else }}
{{- required "externalDatabase.host is required when postgresql.enabled=false" .Values.externalDatabase.host }}
{{- end }}
{{- end }}

{{- define "subs.db.port" -}}
{{- if .Values.postgresql.enabled }}{{ .Values.postgresql.port }}{{ else }}{{ .Values.externalDatabase.port }}{{ end }}
{{- end }}

{{- define "subs.db.name" -}}
{{- if .Values.postgresql.enabled }}{{ .Values.postgresql.auth.database }}{{ else }}{{ .Values.externalDatabase.database }}{{ end }}
{{- end }}

{{- define "subs.db.user" -}}
{{- if .Values.postgresql.enabled }}{{ .Values.postgresql.auth.username }}{{ else }}{{ .Values.externalDatabase.username }}{{ end }}
{{- end }}

{{/* Secret holding `postgres-password`: a user-supplied one, or the one this chart renders. */}}
{{- define "subs.db.secretName" -}}
{{- if and .Values.postgresql.enabled .Values.postgresql.auth.existingSecret }}
{{- .Values.postgresql.auth.existingSecret }}
{{- else if and (not .Values.postgresql.enabled) .Values.externalDatabase.existingSecret }}
{{- .Values.externalDatabase.existingSecret }}
{{- else }}
{{- printf "%s-db" (include "subs.fullname" .) }}
{{- end }}
{{- end }}

{{- define "subs.db.password" -}}
{{- if .Values.postgresql.enabled }}{{ .Values.postgresql.auth.password }}{{ else }}{{ .Values.externalDatabase.password }}{{ end }}
{{- end }}

{{/*
Environment for anything that connects to Postgres. The password stays in the Secret and is spliced in with
Kubernetes' $(VAR) expansion, so the connection string itself never lands in a ConfigMap.
*/}}
{{- define "subs.db.env" -}}
- name: DB_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "subs.db.secretName" .ctx }}
      key: postgres-password
- name: ConnectionStrings__Postgres
  value: {{ printf "Host=%s;Port=%s;Database=%s;Username=%s;Password=$(DB_PASSWORD)%s" (include "subs.db.host" .ctx) (include "subs.db.port" .ctx) (include "subs.db.name" .ctx) (include "subs.db.user" .ctx) .suffix | quote }}
{{- end }}

{{/*
Fails the render when the API's connection pools can outgrow the in-cluster Postgres: every pod (the HPA maximum, plus
one rolling-update surge) may hold config.postgresMaxPoolSize connections.
*/}}
{{- define "subs.db.validatePool" -}}
{{- if .Values.postgresql.enabled }}
{{- $pods := ternary .Values.autoscaling.maxReplicas .Values.replicaCount .Values.autoscaling.enabled | int }}
{{- $needed := mul (add $pods 1) (int .Values.config.postgresMaxPoolSize) }}
{{- $available := sub (int .Values.postgresql.maxConnections) 3 }}
{{- if gt $needed $available }}
{{- fail (printf "API pools need up to %d Postgres connections ((%d pods + 1 surge) x postgresMaxPoolSize %v) but postgresql.maxConnections allows %d; raise postgresql.maxConnections or lower config.postgresMaxPoolSize" $needed $pods .Values.config.postgresMaxPoolSize $available) }}
{{- end }}
{{- end }}
{{- end }}

{{- define "subs.redis.connectionString" -}}
{{- if .Values.redis.enabled }}
{{- printf "%s:%v,abortConnect=false" (include "subs.redis.fullname" .) .Values.redis.port }}
{{- else }}
{{- .Values.externalRedis.connectionString }}
{{- end }}
{{- end }}

{{/* Comma-separated KongPlugin names for the konghq.com/plugins annotation. */}}
{{- define "subs.kong.pluginNames" -}}
{{- $names := list }}
{{- range $key, $p := .Values.kong.plugins }}
{{- if $p.enabled }}
{{- $names = append $names (printf "%s-%s" (include "subs.fullname" $) $key) }}
{{- end }}
{{- end }}
{{- $names = concat $names .Values.kong.extraPlugins }}
{{- join "," $names }}
{{- end }}
