# Network tools

Read this before writing a tool that calls a host listed under **Network
access** in the approved proposal. Write exactly those hosts, and no others, to
`Capabilities:Network:AllowedHosts` as host names without scheme, port, or path.
The smoke baseline freezes that list; a new host needs a new proposal and approval.

## The approved-host client

`Capabilities.cs` is fixed. `Program.cs` builds `ApprovedHttpClient` from the
approved hosts and passes it to `AgentDefinition.CreateTools`; pass it on to the
tools that need it. It sends read-only GET requests and refuses any other host,
plain HTTP outside loopback addresses, credentials in the URL, and redirects. A
body over 64 KiB, a request over 10 seconds, or a failed connection fails the
run, and `--smoke` reports the reason, such as `network_host_not_approved`.

- `GetStringAsync(url, cancellationToken)` returns the body of a successful
  response; any other status fails the run as `network_http_<status>`.
- `GetAsync(url, cancellationToken)` returns `StatusCode` and `Body` for every
  status. Use it when the API reports missing data with a status, and call
  `EnsureSuccessStatusCode()` so any other failure still fails the run.

```csharp
[Description("Get one joke by its JokeAPI ID.")]
public async Task<string> GetJoke(
    [Description("The joke ID.")] int id,
    CancellationToken cancellationToken)
{
    var response = await http.GetAsync(
        $"https://v2.jokeapi.dev/joke/Any?idRange={id}&safe-mode", cancellationToken);
    // JokeAPI answers 400 when no joke has this ID; return that instead of failing the run.
    if (response.StatusCode == 400)
        return "status=not_found; reason=no_joke_with_this_id";
    return response.EnsureSuccessStatusCode().Body;
}
```

Return the API data the model needs, not labels or citations it does not come
from. Never report a network or server failure as "not found": that would let
the `not-found` smoke pass while the API is down.

## Smoke cases for network tools

- Markers are facts that only the API response contains. When the API returns
  random items, give the tool a parameter that selects a fixed item, such as an
  ID, so a smoke prompt can ask for it and check its text.
- The `not-found` case asks for something the API does not have, such as an
  unknown ID, and expects the answer for the tool's not-found result.
- Words in tool descriptions reach the model without a call; do not use them as
  markers.
- Without declared content, the `knowledge` case checks a fact from a tool
  result instead of a source path.
