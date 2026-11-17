using System.Security.Cryptography;

namespace ClaimFlow.Api.Services;

public interface IClaimReferenceGenerator
{
    string Create(DateTime nowUtc);
}

/// <summary>
/// Creates references like CLM-2026-7KQ2MD. The random part avoids a database sequence, which keeps
/// the code identical on SQLite and SQL Server. The unique index on Claims.Reference is the final guard,
/// and <see cref="ClaimService"/> retries on the (very unlikely) collision.
/// </summary>
public sealed class ClaimReferenceGenerator : IClaimReferenceGenerator
{
    // Crockford-style alphabet: no I, L, O or U, so references are easy to read aloud and type.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int RandomLength = 6;

    public string Create(DateTime nowUtc)
    {
        Span<char> random = stackalloc char[RandomLength];
        for (var i = 0; i < random.Length; i++)
        {
            random[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"CLM-{nowUtc.Year}-{random}";
    }
}
