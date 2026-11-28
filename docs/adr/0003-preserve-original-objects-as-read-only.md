# Preserve Original Objects behind a read-only editing boundary

The current editor phase does not expose modifications or deletions of Original Objects. Imported Original Object Edits are retained for round-trip but are not editable or synthesized, while all authoring applies to Editable Overlay Placements. A future capability may expose validated Original Object Edits, but only after path validation and a separate decision; this ADR does not make that capability current.
