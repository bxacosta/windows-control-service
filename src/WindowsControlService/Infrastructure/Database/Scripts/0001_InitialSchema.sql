-- Scripts run in embedded-resource name order, so the numeric prefix is what defines the
-- order, not decoration. There are no down migrations: rollback on a single machine means
-- restoring the previous executable, and the schema only ever grows.

-- Key is the primary key rather than a UNIQUE column beside an AUTOINCREMENT Id. Nobody would
-- ever use that Id, and the separate index on Key would duplicate the uniqueness constraint.
-- Timestamps are ISO 8601 round-trip text, always UTC.
CREATE TABLE Settings (
    Key       TEXT NOT NULL PRIMARY KEY,
    Value     TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
