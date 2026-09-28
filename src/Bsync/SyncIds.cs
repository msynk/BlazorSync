using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Bsync;

/// <summary>Validation rules for document and operation identifiers.</summary>
public static class SyncIds
{
    /// <summary>Maximum identifier length in UTF-16 code units.</summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Returns <see langword="true"/> for a non-empty identifier of at most <see cref="MaxLength"/>
    /// UTF-16 code units, with no control characters and no unpaired surrogates. Non-ASCII text is
    /// allowed; identifiers are compared ordinally.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxLength)
        {
            return false;
        }

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (char.IsControl(c))
            {
                return false;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= id.Length || !char.IsLowSurrogate(id[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless <paramref name="id"/> is valid.</summary>
    public static void Validate([NotNull] string? id, [CallerArgumentExpression(nameof(id))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(id, paramName);
        if (!IsValid(id))
        {
            throw new ArgumentException(
                $"Identifiers must be 1-{MaxLength} UTF-16 code units without control characters or unpaired surrogates.",
                paramName);
        }
    }
}
