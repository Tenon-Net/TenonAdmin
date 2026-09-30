# Route A: Monolithic Deployment

A monolithic deployment lets ASP.NET Core serve both the admin interface and APIs, useful for internal systems managed as one process. Frontend and backend share an origin, so CORS is unnecessary. Complete [production configuration](/guide/deployment/) before hosting the frontend below; keep uploads outside the static directory.

1. Copy `web/dist/*` into the host project's `wwwroot/`.
2. Add two lines of **plain ASP.NET Core** code to your own `Program.cs` (the kernel doesn't manage frontend hosting, so there's no `MapTenonAdminSpa`-style wrapper):

```csharp
var app = builder.Build();

app.UseStaticFiles();               // serve the frontend build under wwwroot
app.MapTenonAdmin();                // API endpoints (the fallback always matches last, whatever the order)
app.MapFallbackToFile("index.html"); // SPA history-mode fallback: unmatched paths go to the frontend

app.Run();
```

3. **You must also move the upload directory out of `wwwroot`**:

```json
{ "TenonAdmin": { "Upload": { "RootPath": "./storage/upload" } } }
```

::: danger Skipping this is an auth bypass
The upload root defaults to `./wwwroot/upload`, while uploaded files are normally fetched through the **authenticated** `GET /api/v1/sys/file/{id}/download` endpoint. Once `UseStaticFiles()` is on, `wwwroot/upload/**` gets served **anonymously** by the static-files middleware — anyone who guesses or obtains the path can download it, and authentication is effectively bypassed.

If you only wanted to host this directory to make **images display**, you **don't need to**: the kernel has a signed direct link, `GET /api/v1/sys/file/{id}/view?sig=…` (the upload endpoint hands it to you directly in the `viewUrl` field) — anonymously fetchable but the signature can't be forged, so `<img src>` works fine while the whole upload directory stays locked down.
:::

After deployment, open `/`, sign in, and refresh a business page to check the route fallback. `/health/ready` should return `Healthy`. Confirm that uploaded files remain accessible only through protected downloads or signed links, not anonymous `/upload/...` URLs. For React, replace `web/dist` with `web-react/dist` in the steps.

