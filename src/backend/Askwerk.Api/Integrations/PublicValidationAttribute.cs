// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Askwerk.Api.Integrations;

/// <summary>
/// Answers a request body that fails its DataAnnotations with a Public API problem (AWK-67): the stable type of the
/// given code, translated title and detail, and errors naming each field (camelCase) with its error codes. Runs before
/// the framework's own 400, which has no stable type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicValidationAttribute : ActionFilterAttribute
{
    // The framework's ModelStateInvalidFilter has order -2000; this one goes first.
    private const int BeforeTheFrameworksFilter = -3000;

    /// <summary>Answers invalid bodies with the problem of this code.</summary>
    /// <param name="code">The problem's code, e.g. contact-invalid.</param>
    public PublicValidationAttribute(string code)
    {
        Code = code;
        Order = BeforeTheFrameworksFilter;
    }

    /// <summary>Gets the problem's code.</summary>
    public string Code { get; }

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => FieldOf(entry.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(entry => entry.Value!.Errors).Select(CodeOf).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        context.Result = Result(context.HttpContext, Code, errors);
    }

    /// <summary>The problem of a request that is not valid, with each field's error codes.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="code">The problem's code.</param>
    /// <param name="errors">Field (camelCase) to error codes.</param>
    /// <returns>A 400 with the problem.</returns>
    public static IActionResult Result(HttpContext httpContext, string code, IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = PublicProblems.Of(httpContext, StatusCodes.Status400BadRequest, code);
        problem.Extensions["errors"] = errors;

        return new ProblemWithErrors(problem);
    }

    /// <summary>Writes the problem itself, so the controller's Produces never turns it into plain JSON.</summary>
    private sealed class ProblemWithErrors(ProblemDetails problem) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return context.HttpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        }
    }

    /// <summary>Our annotations carry codes (email-invalid); the framework's own sentences, e.g. of a body that is no JSON, become invalid.</summary>
    private static string CodeOf(Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error) =>
        error.Exception is not null || string.IsNullOrEmpty(error.ErrorMessage) || error.ErrorMessage.Any(char.IsWhiteSpace) ? "invalid" : error.ErrorMessage;

    /// <summary>"Email", "request.Email" and "$.email" all become email.</summary>
    private static string FieldOf(string key)
    {
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        name = name.Contains('.', StringComparison.Ordinal) ? name[(name.LastIndexOf('.') + 1)..] : name;

        return name.Length == 0 || name == "$" ? "body" : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
