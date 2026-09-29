namespace SubscriptionManagement.Infrastructure;

/// <summary>Collects field errors (camelCase JSON names) and throws the shared 400 problem.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public void NotBlank(string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "must not be blank");
        }
        else if (value.Length > maxLength)
        {
            Add(field, $"size must be between 0 and {maxLength}");
        }
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw ApiProblemException.Validation(
                _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal));
        }
    }
}
