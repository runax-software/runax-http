; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Rules that shipped in a release live in AnalyzerReleases.Shipped.md. Roslyn enforces this
; bookkeeping (RS2008) and it is worth having: these ids reach consumers' build logs and NoWarn
; lists, so retiring one or changing its severity is a breaking change to them. An id is never
; reused for a different meaning, which is why RXH014's retirement adds RXH019 rather than
; repurposing the number.

### Removed Rules

Rule ID | Category   | Severity | Notes
--------|------------|----------|------------------------------------------------------------
RXH014  | Runax.Http | Error    | BodyFormat.UrlEncoded and .Multipart are now emitted, so there is nothing left to refuse

### New Rules

Rule ID | Category   | Severity | Notes
--------|------------|----------|------------------------------------------------------------
RXH019  | Runax.Http | Error    | This parameter type cannot be sent in that body format
RXH020  | Runax.Http | Warning  | This [Retry] will not retry anything
RXH021  | Runax.Http | Error    | [Retry] needs at least one attempt
RXH022  | Runax.Http | Error    | This parameter type cannot be sent as a query value
RXH023  | Runax.Http | Error    | A query object property cannot be sent
