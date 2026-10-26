using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Payments;
using Api.Controllers;
using Api.Repositories;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class PaymentActivityControllerTests
{
    [Fact]
    public async Task InternalOnlySingleRowRouteIsStrictAndUnknownActivityIs404WithoutInventedZeros()
    {
        var rows = new Rows();
        var service = new PaymentActivityService(rows);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["Internal:Secret"] = "synthetic-payment-secret";
        builder.Services.AddSingleton(service);
        builder.Services.AddControllers(options => options.Conventions.Add(new RoutePrefixConvention("company-service")))
            .AddApplicationPart(typeof(PaymentActivityController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var http = app.GetTestClient();
        var input = PaymentRowValidationTests.Row();
        const string write = "/company-service/internal/payment-rows";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(write, input)).StatusCode);
        Assert.Empty(rows.Writes);
        http.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-payment-secret");
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync(write, new StringContent(json[..^1] + ",\"knownAt\":\"spoofed\"}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/company-service/payment-rows", input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(write, input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync(write, input with { SourceRowId = "part-B:row-1" })).StatusCode);
        Assert.Equal(2, rows.Writes.Count);
        Assert.Equal(rows.Writes[0].PayerCompanyId, rows.Writes[1].PayerCompanyId);
        Assert.Equal(rows.Writes[0].BeneficiaryCompanyId, rows.Writes[1].BeneficiaryCompanyId);
        Assert.NotEqual(rows.Writes[0].SourceRowId, rows.Writes[1].SourceRowId);
        var read = $"/company-service/internal/payment-activity/{input.PayerCompanyId:D}";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(read + "?sourceId=one")).StatusCode);
        var query = "?sourceId=" + Uri.EscapeDataString(input.SourceId) + "&sourceVersionId=" + Uri.EscapeDataString(input.SourceVersionId) + "&asOf=2026-10-26T12:00:00Z";
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(read + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(read + query + "&sourceId=duplicate")).StatusCode);
        Assert.Single(rows.Reads);
        Assert.Equal(input.PayerCompanyId, rows.Reads[0].Company);
        Assert.Equal(input.SourceId, rows.Reads[0].Source);
        Assert.Equal(input.SourceVersionId, rows.Reads[0].Version);
    }

    private sealed class Rows : IPaymentRows
    {
        public readonly List<PaymentRowInput> Writes = [];
        public readonly List<(Guid Company, string Source, string Version)> Reads = [];
        public Task<PaymentRowReceipt> Save(PaymentRowInput input)
        {
            Writes.Add(input);
            return Task.FromResult(new PaymentRowReceipt(input, DateTimeOffset.Parse("2026-10-26T10:00:00Z"), Writes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), input.PayerCompanyId == input.BeneficiaryCompanyId));
        }
        public Task<PaymentActivity?> Activity(Guid companyId, string sourceId, string sourceVersionId, DateTimeOffset asOf)
        {
            Reads.Add((companyId, sourceId, sourceVersionId));
            return Task.FromResult<PaymentActivity?>(null);
        }
    }
}
