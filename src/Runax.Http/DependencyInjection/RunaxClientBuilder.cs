using Microsoft.Extensions.DependencyInjection;
using Runax.Http.Retry;
using Runax.Http.Serialization;

namespace Runax.Http.DependencyInjection;

/// <inheritdoc />
internal sealed class RunaxClientBuilder<TContract> : IRunaxClientBuilder<TContract>
    where TContract : class
{
    private readonly RunaxClientOptions _options;

    internal RunaxClientBuilder(IServiceCollection services, RunaxClientOptions options)
    {
        Services = services;
        _options = options;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (!baseAddress.IsAbsoluteUri)
            throw new ArgumentException(
                $"The base address '{baseAddress}' must be absolute.", nameof(baseAddress));

        _options.BaseAddress = baseAddress;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithBaseAddress(string baseAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseAddress);

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var parsed))
            throw new ArgumentException(
                $"'{baseAddress}' is not an absolute URI.", nameof(baseAddress));

        return WithBaseAddress(parsed);
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> UseTransport(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _options.TransportName = name;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        _options.Timeout = timeout;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithDefaultHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        _options.DefaultHeaders[name] = value;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithRetry(int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        _options.Retry.MaxAttempts = maxAttempts;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> WithRetry(Action<RetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options.Retry);

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.Retry.MaxAttempts, 1);

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> UseSerializer(IContentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        _options.Serializer = serializer;

        return this;
    }

    /// <inheritdoc />
    public IRunaxClientBuilder<TContract> Configure(Action<RunaxClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options);

        return this;
    }
}
