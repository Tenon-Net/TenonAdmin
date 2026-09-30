using System.Net.Http.Json;
using System.Text.Json;

namespace TenonAdmin.Tests;

public class IntegrationMockPartnerTests(MockPartnerFixture mock) : IClassFixture<MockPartnerFixture>
{
    [Fact]
    public async Task Concurrent_requests_with_one_key_create_one_ticket_and_replay_its_result()
    {
        using var client = new HttpClient { BaseAddress = new Uri(mock.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", MockPartnerFixture.Token);
        for (var batch = 0; batch < 8; batch++)
        {
            var key = Guid.NewGuid().ToString("N");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = Enumerable.Range(0, 32).Select(async _ =>
            {
                await start.Task;
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tickets")
                {
                    Content = JsonContent.Create(new { title = "并发工单", amount = 1 }),
                };
                request.Headers.Add("Idempotency-Key", key);
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticketNo").GetString();
            }).ToArray();
            start.SetResult();
            var numbers = await Task.WhenAll(calls);
            Assert.Single(numbers.Distinct());
            Assert.Equal(1, mock.Executions(key));
            Assert.Single(mock.State.Tickets.Values, t => t.IdempotencyKey == key);
        }
    }
}
