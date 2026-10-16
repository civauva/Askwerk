// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Identity;

/// <summary>A value broke a domain rule; the API turns it into a problem detail at the field.</summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="field">The field the rule belongs to, as the client names it.</param>
    /// <param name="code">Stable code, e.g. <c>slug-invalid</c>.</param>
    public DomainValidationException(string field, string code)
        : base($"{field}: {code}")
    {
        Field = field;
        Code = code;
    }

    /// <summary>Creates the exception without details (for serialisers).</summary>
    public DomainValidationException()
        : this("", "")
    {
    }

    /// <summary>Creates the exception with a message (for serialisers).</summary>
    /// <param name="message">The message.</param>
    public DomainValidationException(string message)
        : this("", message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception (for serialisers).</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Field = "";
        Code = message;
    }

    /// <summary>The field the rule belongs to.</summary>
    public string Field { get; }

    /// <summary>Stable code of the broken rule.</summary>
    public string Code { get; }
}
