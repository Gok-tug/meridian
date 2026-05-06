using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal static class AspNetCoreRouteResolver
{
    public static string NormalizeHttpMethod(string httpMethod)
    {
        return httpMethod.Trim().ToUpperInvariant();
    }

    public static string NormalizeRouteTemplate(string routeTemplate)
    {
        var normalized = routeTemplate.Trim();
        if (normalized.Length == 0 || normalized.Equals("~", StringComparison.Ordinal))
        {
            return "/";
        }

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    public static string CombineRoutes(params string?[] routeParts)
    {
        var combined = string.Join(
            "/",
            routeParts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim().Trim('/')));

        return NormalizeRouteTemplate(combined);
    }

    public static string CombineMvcRoutes(string? controllerRouteTemplate, string? actionRouteTemplate)
    {
        if (!string.IsNullOrWhiteSpace(actionRouteTemplate) && IsAbsoluteRouteTemplate(actionRouteTemplate))
        {
            return NormalizeRouteTemplate(actionRouteTemplate);
        }

        return CombineRoutes(controllerRouteTemplate, actionRouteTemplate);
    }

    private static bool IsAbsoluteRouteTemplate(string routeTemplate)
    {
        var trimmed = routeTemplate.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.Equals("~", StringComparison.Ordinal) ||
            trimmed.StartsWith("~/", StringComparison.Ordinal);
    }

    public static string ReplaceMvcTokens(string routeTemplate, string controllerName, string actionName)
    {
        var controllerTokenValue = controllerName.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerName[..^"Controller".Length]
            : controllerName;

        return routeTemplate
            .Replace("[controller]", controllerTokenValue, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryResolveString(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var unwrappedExpression = Unwrap(expression);
        var constant = semanticModel.GetConstantValue(unwrappedExpression, cancellationToken);
        if (constant.HasValue && constant.Value is string constantString)
        {
            return constantString;
        }

        var symbol = semanticModel.GetSymbolInfo(unwrappedExpression, cancellationToken).Symbol;
        return symbol switch
        {
            IFieldSymbol { HasConstantValue: true, ConstantValue: string fieldValue } => fieldValue,
            ILocalSymbol { HasConstantValue: true, ConstantValue: string localValue } => localValue,
            IParameterSymbol { HasExplicitDefaultValue: true, ExplicitDefaultValue: string parameterValue } => parameterValue,
            _ => null
        };
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
