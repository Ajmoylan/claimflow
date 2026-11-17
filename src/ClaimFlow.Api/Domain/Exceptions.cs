namespace ClaimFlow.Api.Domain;

/// <summary>
/// Business-rule failures. Each type maps to one HTTP status in
/// <see cref="Infrastructure.ProblemDetailsExceptionHandler"/>, so controllers never build error responses by hand.
/// </summary>
public abstract class ClaimFlowException(string message) : Exception(message);

/// <summary>Maps to 404 Not Found.</summary>
public sealed class NotFoundException(string resource, string key)
    : ClaimFlowException($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;

    public string Key { get; } = key;
}

/// <summary>Maps to 400 Bad Request with field-level errors (ValidationProblemDetails).</summary>
public sealed class DomainValidationException : ClaimFlowException
{
    public DomainValidationException(IDictionary<string, string[]> errors)
        : base("One or more business rules were not met.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public DomainValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Maps to 409 Conflict: the claim is not in a state that allows this change.</summary>
public sealed class InvalidStatusTransitionException(string reference, ClaimStatus from, ClaimStatus to)
    : ClaimFlowException($"Claim {reference} cannot move from {from} to {to}.")
{
    public ClaimStatus From { get; } = from;

    public ClaimStatus To { get; } = to;
}

/// <summary>Maps to 429 Too Many Requests: the public demo's cap on visitor-created claims is reached.</summary>
public sealed class DemoLimitReachedException(int limit)
    : ClaimFlowException($"The demo holds at most {limit} visitor-created claims. It resets automatically every night.")
{
    public int Limit { get; } = limit;
}
