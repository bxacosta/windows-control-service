-- MatchAttribute crosses from a table into the name of an XML attribute in the deployed policy.
-- The code side of that is an enum, and this is the storage side: a value the code never
-- produced -- a hand edit, a future migration, a restored backup -- must not be able to become
-- an arbitrary attribute name, or an exception while the policy is being built, which is the
-- worst possible moment.
--
-- SQLite cannot add a CHECK to an existing table, so the table is rebuilt. No PRAGMA here on
-- purpose: there are no foreign keys, and PRAGMA foreign_keys is a no-op inside the transaction
-- DbUp wraps each script in.
CREATE TABLE BlockedApplications_rebuilt (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Name           TEXT    NOT NULL,
    ExecutablePath TEXT    NOT NULL,
    MatchAttribute TEXT    NOT NULL CHECK (MatchAttribute IN ('FileName', 'InternalName', 'ProductName')),
    MatchValue     TEXT    NOT NULL,
    ProductName    TEXT    NULL,
    IsEnabled      INTEGER NOT NULL DEFAULT 1,
    CreatedAt      TEXT    NOT NULL
);

INSERT INTO BlockedApplications_rebuilt
    (Id, Name, ExecutablePath, MatchAttribute, MatchValue, ProductName, IsEnabled, CreatedAt)
SELECT Id, Name, ExecutablePath, MatchAttribute, MatchValue, ProductName, IsEnabled, CreatedAt
FROM BlockedApplications;

DROP TABLE BlockedApplications;

ALTER TABLE BlockedApplications_rebuilt RENAME TO BlockedApplications;

-- Recreated because dropping the table dropped them with it.
CREATE UNIQUE INDEX ux_blockedapplications_path
    ON BlockedApplications(ExecutablePath COLLATE NOCASE);

CREATE INDEX ix_blockedapplications_enabled
    ON BlockedApplications(IsEnabled);
