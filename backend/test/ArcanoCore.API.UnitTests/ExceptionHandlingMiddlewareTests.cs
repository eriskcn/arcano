using System;
using System.Threading.Tasks;
using ArcanoCore.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class ExceptionHandlingMiddlewareTests
{
    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class AbortSpy : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public System.Threading.CancellationToken RequestAborted { get; set; }
        public void Abort() => Aborted = true;
    }

    [Fact]
    public async Task Failure_after_the_response_started_aborts_the_connection()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        var spy = new AbortSpy();
        context.Features.Set<IHttpRequestLifetimeFeature>(spy);
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("storage failed"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(spy.Aborted);
    }
}
