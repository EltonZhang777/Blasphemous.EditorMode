# Let the runtime creator registry own Content Catalog membership

Status: Accepted.

The Content Catalog uses the Level Framework runtime creator registry as its sole membership authority. It enumerates the internal registry through guarded reflection, preserves exact registry keys, and fills missing display metadata with stable fallback values (`Registered`, `Level Framework`, `Not declared`). The curated membership table and `IsSupported` filtering are retired.

This supersedes the curated-catalog decision recorded in the migration spec and technical overview. Those documents remain historical planning evidence, but their curated-first sections are not current requirements. Guarded reflection is an accepted technical risk: registry, enumeration, and value-access failures remain visible diagnostics, and an unavailable registry must not appear as a silent empty catalog.

Sharing the registry reflection between inventory enumeration and exact lookup is deferred as a low-priority refactor in `#129`; it does not change this membership decision.
