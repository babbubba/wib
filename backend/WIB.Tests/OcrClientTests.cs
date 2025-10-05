using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WIB.Infrastructure.Clients;
using Xunit;

namespace WIB.Tests;

public class OcrClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(new { text = "hello-ocr" });
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task ExtractAsync_Returns_Text_From_Endpoint()
    {
        var handler = new FakeHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var client = new OcrClient(http);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("img"));
        var text = await client.ExtractAsync(ms, CancellationToken.None);
        Assert.Equal("hello-ocr", text);
    }
}

