CREATE TABLE BlockedApplications (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    Name             TEXT    NOT NULL,
    ExecutablePath   TEXT    NOT NULL,
    OriginalFileName TEXT    NOT NULL,
    ProductName      TEXT    NULL,
    IsEnabled        INTEGER NOT NULL DEFAULT 1,
    CreatedAt        TEXT    NOT NULL
);

-- Declared here, in the first migration that creates the table, rather than added later.
-- Adding a uniqueness constraint after the fact means dealing with the duplicate rows that
-- already exist, and that always ends as a try/catch around CREATE INDEX.
CREATE UNIQUE INDEX ux_blockedapplications_path
    ON BlockedApplications(ExecutablePath COLLATE NOCASE);

CREATE INDEX ix_blockedapplications_enabled
    ON BlockedApplications(IsEnabled);
