CREATE TABLE account(
    account_id text PRIMARY KEY,
    name text NOT NULL,
    type text NOT NULL,
    data blob CHECK ( json_valid(data, 8) )
) STRICT;

CREATE TABLE calendar(
    account_id text NOT NULL,
    calendar_id text NOT NULL UNIQUE,
    external_id text,
    name text NOT NULL,
    color text,
    enabled int NOT NULL,
    last_sync int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, calendar_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX calendar_by_external_id ON calendar (account_id, external_id) WHERE external_id IS NOT NULL;

CREATE TABLE calendar_event(
    calendar_id text NOT NULL,
    event_id text NOT NULL NOT NULL UNIQUE,
    external_id text,
    start_time int,
    end_time int,
    title text,
    changed_at int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (calendar_id, event_id),
    FOREIGN KEY (calendar_id) REFERENCES calendar(calendar_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX calendar_event_by_external_id ON calendar_event (calendar_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX calendar_event_by_time ON calendar_event
  (calendar_id, start_time, end_time);

CREATE TABLE calendar_event_relation(
    parent_event_id text NOT NULL,
    child_event_id text NOT NULL,
    PRIMARY KEY (parent_event_id, child_event_id),
    FOREIGN KEY (parent_event_id) REFERENCES calendar_event(event_id) ON DELETE CASCADE,
    FOREIGN KEY (child_event_id) REFERENCES calendar_event(event_id) ON DELETE CASCADE
) STRICT;

CREATE INDEX calendar_event_relation_by_parent ON calendar_event_relation (parent_event_id);
CREATE INDEX calendar_event_relation_by_child ON calendar_event_relation (child_event_id);

CREATE TABLE calendar_event_relation_backlog(
    calendar_id text NOT NULL,
    parent_external_id text NOT NULL,
    child_external_id text NOT NULL,
    PRIMARY KEY (calendar_id, parent_external_id, child_external_id),
    FOREIGN KEY (calendar_id) REFERENCES calendar(calendar_id) ON DELETE CASCADE
) STRICT;

CREATE TABLE reminder(
    reminder_id text NOT NULL PRIMARY KEY,
    -- 1 = calendar
    target_type int NOT NULL CHECK(target_type IN (1)),
    target_id text NOT NULL,
    target_time int NOT NULL,
    trigger_time int NOT NULL
) STRICT;

CREATE INDEX reminder_by_time ON reminder (trigger_time);
CREATE INDEX reminder_by_type_and_id ON reminder (target_type, target_id);

CREATE TRIGGER reminder_cleanup_calendar
    AFTER DELETE ON calendar_event
    FOR EACH ROW
    BEGIN
       DELETE FROM reminder WHERE target_type = 1 AND target_id = OLD.event_id;
    END;
