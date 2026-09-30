namespace TenonAdmin.Samples.MockPartner;

/// <summary>独立运行入口:<c>dotnet run --project backend/samples/IntegrationMockPartner</c>(默认 http://127.0.0.1:5300)。</summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = MockPartnerServer.Build(args, url: Environment.GetEnvironmentVariable("MOCK_PARTNER_URL") ?? "http://127.0.0.1:5300");
        await app.RunAsync();
    }
}
