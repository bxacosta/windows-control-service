-- A WDAC deny rule with FileName= does not compare against the name of the file on disk: it
-- compares against the OriginalFilename embedded in the binary's version resource. Plenty of
-- shipped executables carry no OriginalFilename at all, and for those the rule has to match on
-- InternalName or ProductName instead -- so which attribute was used stops being an implicit
-- constant and becomes part of the row.
--
-- The old column held a value that was sometimes the embedded name and sometimes a guess at it
-- from the path. Renaming it is the point: the new name cannot be read as "the file name".
ALTER TABLE BlockedApplications ADD COLUMN MatchAttribute TEXT NOT NULL DEFAULT 'FileName';

ALTER TABLE BlockedApplications RENAME COLUMN OriginalFileName TO MatchValue;
