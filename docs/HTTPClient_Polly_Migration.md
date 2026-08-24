# Migrate to IHttpClientFactory + Polly

Goal: replace ad-hoc `HttpClient` usage with `IHttpClientFactory` and resilient policies using `Polly`.

Why:
- Centralized connection management, DNS refresh handling, socket reuse.
- Add retry/circuit-breaker/timeouts consistently.
- Easier unit testing via typed/named clients.

Packages to add:

```powershell
dotnet add package Polly
dotnet add package Polly.Extensions.Http
```

DI registration (example for `App.xaml.cs` or Host builder):

```csharp
using Polly;
using Polly.Extensions.Http;

var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) });

var circuitBreaker = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));

services.AddHttpClient("telegram", client => {
    client.BaseAddress = new Uri("https://api.telegram.org");
    client.Timeout = TimeSpan.FromSeconds(15);
})
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(circuitBreaker);

services.AddHttpClient("scraper", client => {
    client.Timeout = TimeSpan.FromSeconds(30);
})
    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError().RetryAsync(2));
```

Refactor `TelegramNotificationService` (before -> after):

Before (typical anti-pattern):

```csharp
public class TelegramNotificationService {
    private readonly string _token;
    public TelegramNotificationService(string token) {
        _token = token;
    }

    public async Task Send(string message) {
        using var c = new HttpClient();
        await c.PostAsync(...);
    }
}
```

After (injected `HttpClient` via named client):

```csharp
public class TelegramNotificationService {
    private readonly HttpClient _http;
    private readonly string _botToken;

    public TelegramNotificationService(IHttpClientFactory httpFactory, IConfiguration config) {
        _http = httpFactory.CreateClient("telegram");
        _botToken = config["Telegram:BotToken"] ?? throw new ArgumentNullException("Telegram:BotToken");
    }

    public async Task SendAsync(string chatId, string text) {
        var url = $"/bot{_botToken}/sendMessage";
        var payload = new { chat_id = chatId, text };
        var res = await _http.PostAsJsonAsync(url, payload);
        res.EnsureSuccessStatusCode();
    }
}
```

Refactor `ScraperAgent` to use `IHttpClientFactory` similarly:

```csharp
public class ScraperAgent : BackgroundService {
    private readonly HttpClient _http;

    public ScraperAgent(IHttpClientFactory httpFactory) {
        _http = httpFactory.CreateClient("scraper");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var html = await _http.GetStringAsync("https://example.com/listings");
        // parse...
    }
}
```

Testing tip:
- For unit tests, register a named `HttpClient` using `HttpMessageHandler` mocks (e.g., `RichardSzalay.MockHttp`), or use `IHttpClientFactory` mock wrappers.

Follow-up actions (recommended):
- Update `App.xaml.cs` (Host builder) to register named clients and configure policies.
- Modify `TelegramNotificationService` and `ScraperAgent` to accept `IHttpClientFactory` or `HttpClient` via constructor injection.
- Run build and unit tests; tweak timeouts and retry counts based on observed behavior.
