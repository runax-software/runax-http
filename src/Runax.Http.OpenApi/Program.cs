using Runax.Http.OpenApi;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-runax-http");

// InvokeAsync owns Ctrl+C: it cancels the token every action receives and waits for it to stop.
return await Cli.Build(http).Parse(args).InvokeAsync().ConfigureAwait(false);
