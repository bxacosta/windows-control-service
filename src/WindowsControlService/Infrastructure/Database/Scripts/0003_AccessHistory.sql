-- Kind and Origin are stored as text rather than integers: this table gets read by hand when
-- something looks wrong, and "Logon" / "Remote" mean something without consulting an enum.
CREATE TABLE LogonEvents (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Channel    TEXT    NOT NULL,
    RecordId   INTEGER NOT NULL,
    EventId    INTEGER NOT NULL,
    Kind       TEXT    NOT NULL,
    OccurredAt TEXT    NOT NULL,
    UserName   TEXT    NOT NULL,
    SessionId  INTEGER NULL,
    Address    TEXT    NULL,
    Origin     TEXT    NOT NULL,

    -- OccurredAt is part of the key on purpose. When the Windows event log is cleared the
    -- RecordId counter restarts at 1, and without the date the new events would collide with
    -- the stored ones and be discarded silently as duplicates.
    UNIQUE (Channel, RecordId, OccurredAt)
);

CREATE INDEX ix_logonevents_occurred ON LogonEvents(OccurredAt DESC);
