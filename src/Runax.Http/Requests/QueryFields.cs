namespace Runax.Http.Requests;

/// <summary>
/// Turns a dictionary-shaped <c>[Query]</c> argument into query entries, for generated code.
/// </summary>
/// <remarks>
/// <para>
/// The one query shape whose <em>keys</em> are not known until run time. An object's fields are
/// resolved by the generator and emitted one by one; a dictionary's are whatever the caller put in
/// it, so the pairs have to be produced here and spliced into the request.
/// </para>
/// <para>
/// Still reflection-free: the argument is already a sequence of key/value pairs, so this only
/// re-shapes what it was handed. Values keep their static type as <see cref="object"/> and are
/// formatted later by the same formatter as every other query value.
/// </para>
/// </remarks>
public static class QueryFields
{
    /// <summary>Re-shapes a dictionary-like argument into query entries.</summary>
    /// <typeparam name="TValue">The dictionary's value type.</typeparam>
    /// <param name="fields">The entries, or <see langword="null"/>.</param>
    /// <returns>
    /// One entry per pair, in the order the sequence yields them. Empty for a null argument, which
    /// is how a null query object already behaves.
    /// </returns>
    /// <remarks>
    /// A null <em>value</em> is kept rather than dropped here, because dropping it is the query
    /// builder's job — it omits a null value for every other shape too, and duplicating that rule
    /// in two places is how the two stop agreeing.
    /// </remarks>
    public static IEnumerable<KeyValuePair<string, object?>> From<TValue>(
        IEnumerable<KeyValuePair<string, TValue>>? fields)
    {
        if (fields is null)
            return [];

        var entries = new List<KeyValuePair<string, object?>>();

        foreach (var (key, value) in fields)
            entries.Add(new KeyValuePair<string, object?>(key, value));

        return entries;
    }
}
