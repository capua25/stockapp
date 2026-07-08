using System.Net;
using StockApp.Api.Tests.Fixtures;
using Xunit;

namespace StockApp.Api.Tests;

public class HealthEndpointTests : ApiTestBase
{
    public HealthEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetRaiz_DevuelveOk()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
