using System.Text.Json;
using System.Text.Json.Serialization;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Features.Invoices.GetInvoiceRenewal;
using SubscriptionManagement.Features.Invoices.SettleInvoice;
using SubscriptionManagement.Features.Plans;
using SubscriptionManagement.Features.Plans.CreatePlan;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;
using SubscriptionManagement.Features.Subscriptions.RenewSubscription;
using SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;

namespace SubscriptionManagement.Infrastructure;

/// <summary>
/// Source-generated System.Text.Json metadata for every type that crosses the wire, the cache or the outbox (Native AOT).
/// Web defaults = camelCase names, case-insensitive binding — the same behaviour the original minimal API had.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateSubscriptionCommand))]
[JsonSerializable(typeof(SubscriptionResponse))]
[JsonSerializable(typeof(SearchSubscriptionsQuery))]
[JsonSerializable(typeof(SearchSubscriptionsResult))]
[JsonSerializable(typeof(RenewSubscriptionResult))]
[JsonSerializable(typeof(InvoiceResponse))]
[JsonSerializable(typeof(InvoiceList))]
[JsonSerializable(typeof(CreatePlanCommand))]
[JsonSerializable(typeof(PlanResponse))]
[JsonSerializable(typeof(PlanList))]
[JsonSerializable(typeof(SettleInvoiceRequest))]
[JsonSerializable(typeof(RenewalStatusResponse))]
[JsonSerializable(typeof(InvoiceIssued))]
[JsonSerializable(typeof(SubscriptionCanceled))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;