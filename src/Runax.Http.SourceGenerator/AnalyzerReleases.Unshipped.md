; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Every diagnostic the generator can report is tracked here. Roslyn enforces this for analyzer
; packages (RS2008) and it is worth having: these ids end up in consumers' build logs and
; NoWarn lists, so changing one's severity or retiring it is a breaking change to them.

### New Rules

Rule ID | Category   | Severity | Notes
--------|------------|----------|------------------------------------------------------------
RXH001  | Runax.Http | Error    | Request attributes are only valid on interfaces
RXH002  | Runax.Http | Error    | Unsupported client method return type
RXH003  | Runax.Http | Error    | Path template placeholder has no parameter
RXH004  | Runax.Http | Error    | A request can have only one body
RXH005  | Runax.Http | Error    | Parameter is not bound to anything
RXH006  | Runax.Http | Error    | A method can take only one CancellationToken
RXH007  | Runax.Http | Error    | Client interfaces cannot be generic
RXH008  | Runax.Http | Error    | Client methods cannot be generic
RXH009  | Runax.Http | Error    | Client interface member has no request attribute
RXH010  | Runax.Http | Warning  | This method's HTTP method does not normally carry a body
RXH011  | Runax.Http | Error    | [BaseAddress] must be an absolute URI
RXH012  | Runax.Http | Error    | Two parameters are bound to the same placeholder
RXH013  | Runax.Http | Error    | Client interfaces can only declare methods
RXH014  | Runax.Http | Error    | This body format is not implemented yet
