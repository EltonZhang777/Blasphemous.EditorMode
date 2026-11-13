# Preserve additions the editor cannot model

When the current editor profile cannot fully represent or validate an addition, retain that addition's original JSON value instead of partially reconstructing it. The addition remains outside the editable collection and does not block compatible additions; export reuses the retained value, although JSON formatting may normalize. This fail-closed choice accepts some uneditable content to avoid silent data loss and semantic drift.
