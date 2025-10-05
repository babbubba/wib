using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WIB.Application.Contracts.Kie;
using WIB.Infrastructure.Clients;
using Xunit;

namespace WIB.Tests;

public class KieClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = new ReceiptDraft
            {
                Store = new ReceiptDraftStore { Name = "X" },
                Datetime = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                Currency = "EUR",
                Lines =
                [
                    new ReceiptDraftLine { LabelRaw = "A", Qty = 1, UnitPrice = 1, LineTotal = 1 }
                ],
                Totals = new ReceiptDraftTotals { Subtotal = 1, Tax = 0, Total = 1 }
            };
            var json = JsonSerializer.Serialize(payload);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task ExtractFieldsAsync_Parses_Draft()
    {
        var http = new HttpClient(new FakeHandler()) { BaseAddress = new Uri("http://test") };
        var client = new KieClient(http);
        var draft = await client.ExtractFieldsAsync("ocr", CancellationToken.None);
        Assert.Equal("X", draft.Store.Name);
        Assert.Single(draft.Lines);
        Assert.Equal(1, draft.Totals.Total);
    }
}

