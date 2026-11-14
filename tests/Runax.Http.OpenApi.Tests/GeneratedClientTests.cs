using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.OpenApi.Tests.Bookshop;
using Runax.Http.Transports;
using Results = Runax.Http.OpenApi.Tests.BookshopResults;

namespace Runax.Http.OpenApi.Tests;

/// <summary>
/// The generated Bookshop client, implemented by the real generator and run through the real
/// runtime with the generated JSON context, against what the document says goes on the wire.
/// </summary>
public class GeneratedClientTests
{
    private const string BookJson = """
        {
          "id": "8f14e45f-ceea-467f-a2a3-1c2b3d4e5f60",
          "title": "Dune",
          "author_ids": [7],
          "genre": "fiction",
          "price": { "amount": 9.99, "currency": "EUR" },
          "created_at": "2026-01-02T03:04:05+00:00",
          "rating": null,
          "series": { "name": "Dune", "position": 1 }
        }
        """;

    [Fact]
    public async Task The_base_address_comes_from_the_first_server_with_its_variables_filled_in()
    {
        var (client, transport) = Create<IBookshopClient>(_ => Json("[]"));

        await client.ListBooksAsync();

        transport.Last.RequestUri.ToString().ShouldBe("https://eu.bookshop.example.com/v2/books");
    }

    [Fact]
    public async Task Query_keys_and_headers_are_the_ones_the_document_names()
    {
        var (client, transport) = Create<IBookshopClient>(_ => Json("[]"));
        var requestId = Guid.Parse("8f14e45f-ceea-467f-a2a3-1c2b3d4e5f60");

        await client.ListBooksAsync(
            Genre.NonFiction, perPage: 50, tag: ["signed", "first edition"], publishedAfter: new DateOnly(2020, 1, 31),
            xRequestID: requestId);

        transport.Last.RequestUri.Query.ShouldBe(
            "?genre=non-fiction&per_page=50&tag=signed&tag=first%20edition&published_after=2020-01-31");
        transport.Last.Headers.ShouldContain(new KeyValuePair<string, string>("X-Request-ID", requestId.ToString()));
    }

    [Fact]
    public async Task A_query_object_is_expanded_under_its_documented_keys()
    {
        var (client, transport) = Create<IBookshopClient>(_ => Json("{}"));

        await client.SearchBooksAsync(new BookFilter
        {
            TitleContains = "dune",
            MinPrice = 5,
            Genres = [Genre.Fiction, Genre.Poetry]
        });

        transport.Last.RequestUri.Query.ShouldBe("?title_contains=dune&min_price=5&genres=fiction&genres=poetry");
    }

    [Fact]
    public async Task A_snake_case_path_parameter_and_a_keyword_named_query_parameter_bind()
    {
        var (client, transport) = Create<IBookshopClient>(request =>
            Json(request.RequestUri.AbsolutePath.StartsWith("/v2/authors", StringComparison.Ordinal) ? "{}" : BookJson));
        var id = Guid.Parse("8f14e45f-ceea-467f-a2a3-1c2b3d4e5f60");

        await client.GetBookAsync(id);
        transport.Last.RequestUri.AbsolutePath.ShouldBe($"/v2/books/{id}");

        await client.GetAuthorAsync(42, @class: "living");
        transport.Last.RequestUri.PathAndQuery.ShouldBe("/v2/authors/42?class=living");
    }

    [Fact]
    public async Task A_response_reads_through_the_wire_names_and_merged_allOf()
    {
        var (client, _) = Create<IBookshopClient>(_ => Json(BookJson));

        var book = await client.GetBookAsync(Guid.NewGuid());

        book.Title.ShouldBe("Dune");
        book.AuthorIds.ShouldBe([7L]);
        book.Genre.ShouldBe(Genre.Fiction);
        book.Price.ShouldBe(new Money { Amount = 9.99m, Currency = "EUR" });
        book.CreatedAt.ShouldBe(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        book.Rating.ShouldBeNull();
        book.Series.ShouldBe(new BookSeries { Name = "Dune", Position = 1 });
    }

    [Fact]
    public async Task A_request_body_uses_wire_names_and_leaves_unset_fields_out()
    {
        var (client, transport) = Create<IBookshopClient>(_ => Json(BookJson, 201));

        await client.CreateBookAsync(new NewBook { Title = "Dune", AuthorIds = [7], Genre = Genre.Fiction });

        Body(transport.Last).ShouldBe("""{"title":"Dune","author_ids":[7],"genre":"fiction"}""");
    }

    [Fact]
    public async Task Form_raw_and_multipart_bodies_are_sent_in_their_documented_formats()
    {
        var (client, transport) = Create<IBookshopClient>(
            _ => Json("""{"id":"o-1","status":"pending"}"""),
            TransportCapabilities.Multipart | TransportCapabilities.RequestStreaming);

        var order = await client.PlaceOrderAsync(new Dictionary<string, string> { ["book_id"] = "b-1", ["quantity"] = "2" });
        order.Status.ShouldBe(OrderStatus.Pending);
        ((FormContent)transport.Last.Content!).Fields.ShouldBe(
            [new("book_id", "b-1"), new("quantity", "2")]);

        using var image = new MemoryStream([1, 2, 3]);
        await client.PutBooksByBookIdCoverAsync(Guid.NewGuid(), image);
        transport.Last.Headers.ShouldContain(new KeyValuePair<string, string>("Content-Type", "image/png"));
        transport.Last.Content.ShouldBeOfType<StreamedContent>();

        await client.ImportCatalogueAsync([new MultipartPart("dry_run", RequestContent.FromString("true"))]);
        transport.Last.Content.ShouldBeOfType<MultipartFormContent>();
    }

    [Fact]
    public async Task A_non_JSON_response_comes_back_as_status_and_headers()
    {
        var (client, _) = Create<IBookshopClient>(_ => new TransportResponse(200) { Content = new MemoryStream([137, 80]) });

        var cover = await client.GetCoverAsync(Guid.NewGuid());

        cover.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task With_api_result_each_documented_response_deserializes_to_its_own_type()
    {
        var (client, _) = Create<Results.IBookshopResultsClient>(request => request.Method == RequestMethod.Post
            ? Json("""{"title":"Unprocessable","status":422,"detail":"title is required"}""", 422)
            : Json(BookJson));

        var rejected = await client.CreateBookAsync(new Results.NewBook { Title = "", AuthorIds = [] });

        rejected.ValueType.ShouldBe(typeof(Results.Problem));
        ((Results.Problem)rejected.Value!).Detail.ShouldBe("title is required");

        var found = await client.GetBookAsync(Guid.NewGuid());

        found.ValueType.ShouldBe(typeof(Results.Book));
    }

    private static (TClient Client, RecordingTransport Transport) Create<TClient>(
        Func<TransportRequest, TransportResponse> respond,
        TransportCapabilities capabilities = TransportCapabilities.None)
        where TClient : class
    {
        var transport = new RecordingTransport(respond, capabilities);
        var services = new ServiceCollection();

        // The registration the tool prints and the generated docs recommend.
        services.AddRunaxHttp()
            .UseSystemTextJson(typeof(TClient) == typeof(IBookshopClient)
                ? BookshopJsonContext.Default.Options
                : Results.BookshopResultsJsonContext.Default.Options)
            .AddTransport(transport)
            .UseDefaultTransport(transport.Name);

        services.AddRunaxClient<TClient>();

        return (services.BuildServiceProvider().GetRequiredService<TClient>(), transport);
    }

    private static TransportResponse Json(string body, int status = 200)
        => new(status)
        {
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
        };

    private static string Body(TransportRequest request)
        => Encoding.UTF8.GetString(((BinaryContent)request.Content!).Bytes.Span);
}

/// <summary>A transport that records what it was asked to send and answers from a script.</summary>
internal sealed class RecordingTransport(
    Func<TransportRequest, TransportResponse> respond,
    TransportCapabilities capabilities) : IHttpTransport
{
    public string Name => "recording";

    public TransportCapabilities Capabilities => capabilities;

    internal List<TransportRequest> Requests { get; } = [];

    internal TransportRequest Last => Requests[^1];

    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
