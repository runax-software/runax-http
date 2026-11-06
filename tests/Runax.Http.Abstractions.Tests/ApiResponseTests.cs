using Runax.Http;

namespace Runax.Http.Abstractions.Tests;

public class ApiResponseTests
{
    private sealed record Repository(string Name);

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public void Success_is_the_2xx_range(int statusCode, bool expected)
        => new ApiResponse(statusCode).IsSuccessStatusCode.ShouldBe(expected);

    [Fact]
    public void EnsureSuccess_returns_the_body_on_success()
    {
        var response = new ApiResponse<Repository>(200) { Content = new Repository("runax-http") };

        response.EnsureSuccess().Name.ShouldBe("runax-http");
    }

    [Fact]
    public void EnsureSuccess_throws_the_error_the_throwing_form_would_have_thrown()
    {
        var error = new HttpApiException("Not found.") { StatusCode = 404 };
        var response = new ApiResponse<Repository>(404) { Error = error };

        var thrown = Should.Throw<HttpApiException>(() => response.EnsureSuccess());

        thrown.ShouldBeSameAs(error);
    }

    [Fact]
    public void EnsureSuccess_will_not_invent_a_body_that_was_not_there()
    {
        // A 204 on a method declaring ApiResponse<T> is a success with nothing to return.
        // Returning null! from a non-nullable signature would push the failure to the caller's
        // next dereference, somewhere with no context about the request.
        var response = new ApiResponse<Repository>(204);

        Should.Throw<InvalidOperationException>(() => response.EnsureSuccess())
            .Message.ShouldContain("204");
    }

    [Fact]
    public void A_non_success_response_carries_the_detail_needed_to_diagnose_it()
    {
        var response = new ApiResponse<Repository>(422)
        {
            ReasonPhrase = "Unprocessable Entity",
            TransportName = "restsharp",
            Headers = [new KeyValuePair<string, string>("X-Request-Id", "abc123")],
            Error = new HttpApiException("Validation failed.")
            {
                StatusCode = 422,
                ResponseContent = """{"errors":["name is required"]}""",
                Method = RequestMethod.Post,
                RequestUri = new Uri("https://api.example.com/repos")
            }
        };

        response.IsSuccessStatusCode.ShouldBeFalse();
        response.Content.ShouldBeNull();
        response.TransportName.ShouldBe("restsharp");
        response.Headers.ShouldContain(h => h.Key == "X-Request-Id");
        response.Error!.ResponseContent.ShouldContain("name is required");
        response.Error.Method.ShouldBe(RequestMethod.Post);
    }

    [Fact]
    public void Headers_default_to_empty_rather_than_null()
    {
        new ApiResponse(200).Headers.ShouldBeEmpty();
        new HttpApiException().ResponseHeaders.ShouldBeEmpty();
    }
}
