using System.Reflection;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Outbox;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>Conventional routing has one shared queue per contract, so every fact and command needs one owner.</summary>
public class ContractRoutingTests
{
  [Fact]
  public void Every_contract_record_including_API_outbox_facts_has_exactly_one_consuming_service()
  {
    var contracts = typeof(InvoiceIssued).Assembly.GetExportedTypes()
      .Where(
        type => type.GetMethod(
          "<Clone>$",
          BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        ) is not null
      )
      .ToArray();
    Assembly[] services =
    [
      typeof(Billing.ServiceSetup).Assembly, typeof(Payments.ServiceSetup).Assembly, typeof(ServiceSetup).Assembly
    ];

    Assert.NotEmpty(contracts);
    foreach (var contract in contracts)
    {
      var consumers = services.Where(
          assembly => assembly.GetExportedTypes()
            .SelectMany(
              type => type.GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly
              )
            )
            .Any(
              method => method.Name is "Handle"
                  or "HandleAsync"
                  or "Start"
                  or "StartAsync"
                  or "Consume"
                  or "ConsumeAsync"
                && method.GetParameters().FirstOrDefault()?.ParameterType == contract
            )
        )
        .ToArray();

      Assert.True(
        consumers.Length == 1,
        $"{contract.FullName} must have exactly one consuming service; found: {string.Join(", ", consumers.Select(assembly => assembly.GetName().Name))}"
      );
    }

    var outboxTypes = typeof(OutboxMessageTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
      .Where(field => field.IsLiteral && field.FieldType == typeof(string))
      .Select(field => (string)field.GetRawConstantValue()!)
      .ToArray();
    Assert.NotEmpty(outboxTypes);
    Assert.Equal(outboxTypes.Order(), OutboxDrain.MessageTypes.Keys.Order());
    Assert.All(
      OutboxDrain.MessageTypes,
      entry =>
      {
        Assert.Equal(entry.Key, entry.Value.Name);
        Assert.Contains(entry.Value, contracts);
      }
    );
  }
}