#nullable enable

namespace Microsoft.AspNetCore.Mvc
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RouteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NonActionAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpGetAttribute : Attribute
    {
        public HttpGetAttribute()
        {
        }

        public HttpGetAttribute(string template)
        {
            Template = template;
        }

        public string? Template { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpPostAttribute : Attribute
    {
        public HttpPostAttribute()
        {
        }

        public HttpPostAttribute(string template)
        {
            Template = template;
        }

        public string? Template { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpPutAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpDeleteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpPatchAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    public interface IActionResult;

    public sealed class OkObjectResult : IActionResult;

    public abstract class Controller
    {
        protected IActionResult Ok(object? value)
        {
            return new OkObjectResult();
        }
    }
}

namespace Microsoft.AspNetCore.Http
{
    public interface IResult;

    internal sealed class OkResult : IResult;

    public static class Results
    {
        public static IResult Ok(object? value)
        {
            return new OkResult();
        }
    }
}

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder;
}

namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.AspNetCore.Routing;

    public sealed class RouteHandlerBuilder;

    public sealed class RouteGroupBuilder : IEndpointRouteBuilder;

    public static class EndpointRouteBuilderExtensions
    {
        public static RouteGroupBuilder MapGroup(this IEndpointRouteBuilder app, string prefix)
        {
            return new RouteGroupBuilder();
        }

        public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder app, string pattern, Delegate handler)
        {
            return new RouteHandlerBuilder();
        }

        public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder app, string pattern, Delegate handler)
        {
            return new RouteHandlerBuilder();
        }

        public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder app, string pattern, Delegate handler)
        {
            return new RouteHandlerBuilder();
        }

        public static RouteHandlerBuilder MapDelete(this IEndpointRouteBuilder app, string pattern, Delegate handler)
        {
            return new RouteHandlerBuilder();
        }

        public static RouteHandlerBuilder MapPatch(this IEndpointRouteBuilder app, string pattern, Delegate handler)
        {
            return new RouteHandlerBuilder();
        }
    }
}

namespace FastEndpoints
{
    public abstract class Endpoint<TRequest, TResponse>
    {
        public virtual void Configure()
        {
        }

        protected void Get(string route)
        {
        }

        protected void Post(string route)
        {
        }

        public virtual Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(TResponse)!);
        }

        public virtual Task HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

namespace MinimalApi.Endpoint
{
    using Microsoft.AspNetCore.Routing;

    public interface IEndpoint<TResult, TRequest, TRepository>
    {
        void AddRoute(IEndpointRouteBuilder app);

        Task<TResult> HandleAsync(TRequest request, TRepository repository);
    }
}
