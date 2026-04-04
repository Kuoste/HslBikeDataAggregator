using System.Net;
using System.Security.Claims;

using Azure.Core.Serialization;

using HslBikeDataAggregator.Functions;
using HslBikeDataAggregator.Services;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace HslBikeDataAggregator.Tests.Functions;

public sealed class StationsFunctionsTests
{
    [Fact]
    public async Task GetStations_ReturnsOkResponseWithCorsHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services
            .AddOptions<WorkerOptions>()
            .Configure(options => options.Serializer = new JsonObjectSerializer());

        var serviceProvider = services.BuildServiceProvider();

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupProperty(context => context.InstanceServices, serviceProvider);

        var responseHeaders = new HttpHeadersCollection();
        var responseBody = new MemoryStream();
        var response = new Mock<HttpResponseData>(functionContext.Object);
        response.SetupProperty(httpResponse => httpResponse.StatusCode);
        response.SetupProperty(httpResponse => httpResponse.Body, responseBody);
        response.SetupGet(httpResponse => httpResponse.Headers).Returns(responseHeaders);
        response.SetupGet(httpResponse => httpResponse.Cookies).Returns(Mock.Of<HttpCookies>());

        var request = new Mock<HttpRequestData>(functionContext.Object);
        request.Setup(httpRequest => httpRequest.CreateResponse()).Returns(response.Object);
        request.SetupGet(httpRequest => httpRequest.Body).Returns(Stream.Null);
        request.SetupGet(httpRequest => httpRequest.Headers).Returns(new HttpHeadersCollection());
        request.SetupGet(httpRequest => httpRequest.Identities).Returns(Array.Empty<ClaimsIdentity>());
        request.SetupGet(httpRequest => httpRequest.Method).Returns("GET");
        request.SetupGet(httpRequest => httpRequest.Url).Returns(new Uri("https://localhost/api/stations"));

        var function = new StationsFunctions(new AggregatedBikeDataService(), NullLogger<StationsFunctions>.Instance);

        var responseData = await function.GetStations(request.Object, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, responseData.StatusCode);
        Assert.True(responseData.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://kuoste.github.io", origins);

        responseData.Body.Position = 0;
        using var reader = new StreamReader(responseData.Body);
        Assert.Equal("[]", await reader.ReadToEndAsync(cancellationToken));
    }
}
