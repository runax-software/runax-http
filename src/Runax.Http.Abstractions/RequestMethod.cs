namespace Runax.Http;

/// <summary>
/// The HTTP method of a request, as an enum rather than a string so that transports can map it
/// to their own representation without parsing.
/// </summary>
public enum RequestMethod
{
    /// <summary>HTTP GET.</summary>
    Get,

    /// <summary>HTTP POST.</summary>
    Post,

    /// <summary>HTTP PUT.</summary>
    Put,

    /// <summary>HTTP PATCH.</summary>
    Patch,

    /// <summary>HTTP DELETE.</summary>
    Delete,

    /// <summary>HTTP HEAD.</summary>
    Head,

    /// <summary>HTTP OPTIONS.</summary>
    Options
}
