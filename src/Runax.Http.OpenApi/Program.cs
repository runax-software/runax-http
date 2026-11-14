using Runax.Http.OpenApi;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-runax-http");

return await Cli.RunAsync(args, Console.Out, Console.Error, http, cancellation.Token).ConfigureAwait(false);
