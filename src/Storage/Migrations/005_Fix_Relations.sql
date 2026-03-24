-- rewrite json data of google events to jsonb
UPDATE calendar_event
SET data = jsonb_set(coalesce(data, jsonb_object()), '$.rawData', jsonb(data ->> '$.rawData'))
WHERE event_id IN (
    SELECT ce.event_id FROM calendar_event ce
        JOIN calendar c ON c.calendar_id = ce.calendar_id
        JOIN account a ON a.account_id = c.account_id
    WHERE a.type == 'Google'
);

-- cleanup invalid relations
DELETE FROM calendar_event_relation
WHERE (parent_event_id, child_event_id) IN (
    SELECT cer.parent_event_id, cer.child_event_id from calendar_event_relation cer
        JOIN calendar_event ce ON ce.event_id = cer.child_event_id
        JOIN calendar_event pe ON pe.event_id = cer.parent_event_id
        JOIN calendar c ON c.calendar_id = ce.calendar_id
        JOIN account a ON a.account_id = c.account_id
    WHERE
        a.type == 'Google' AND
        ce.data ->> '$.rawData.recurringEventId' != pe.external_id
);

-- ensure we no longer create duplicate relations
CREATE UNIQUE INDEX calendar_event_relation_unique_child
    ON calendar_event_relation (child_event_id);