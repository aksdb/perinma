ALTER TABLE account ADD COLUMN capabilities int NOT NULL DEFAULT 0;

UPDATE account
SET capabilities = CASE lower(type)
    WHEN 'google' THEN 3
    WHEN 'caldav' THEN 1
    WHEN 'carddav' THEN 2
    WHEN 'jmap' THEN 4
    ELSE 0
END;

CREATE TABLE mailbox(
    account_id text NOT NULL,
    mailbox_id text NOT NULL UNIQUE,
    external_id text,
    parent_external_id text,
    name text NOT NULL,
    role text,
    unread_count int NOT NULL DEFAULT 0,
    total_count int NOT NULL DEFAULT 0,
    enabled int NOT NULL DEFAULT 1,
    last_sync int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, mailbox_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX mailbox_by_external_id
    ON mailbox (account_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX mailbox_by_parent_external_id
    ON mailbox (account_id, parent_external_id) WHERE parent_external_id IS NOT NULL;

CREATE TABLE mail_thread(
    account_id text NOT NULL,
    thread_id text NOT NULL UNIQUE,
    external_id text,
    subject text,
    participants_summary text,
    preview text,
    latest_message_received_at int,
    unread_count int NOT NULL DEFAULT 0,
    message_count int NOT NULL DEFAULT 0,
    has_attachments int NOT NULL DEFAULT 0,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, thread_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX mail_thread_by_external_id
    ON mail_thread (account_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX mail_thread_by_latest_message_received_at
    ON mail_thread (account_id, latest_message_received_at DESC);

CREATE TABLE mail_message(
    account_id text NOT NULL,
    thread_id text NOT NULL,
    message_id text NOT NULL UNIQUE,
    external_id text,
    internet_message_id text,
    subject text,
    sender_name text,
    sender_address text,
    sent_at int,
    received_at int,
    preview text,
    plain_text_body text,
    html_body text,
    body_fetched_at int,
    has_html_body int NOT NULL DEFAULT 0,
    has_plain_text_body int NOT NULL DEFAULT 0,
    has_attachments int NOT NULL DEFAULT 0,
    has_external_resources int NOT NULL DEFAULT 0,
    has_blocked_content int NOT NULL DEFAULT 0,
    is_unread int NOT NULL DEFAULT 0,
    is_starred int NOT NULL DEFAULT 0,
    is_answered int NOT NULL DEFAULT 0,
    is_draft int NOT NULL DEFAULT 0,
    changed_at int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, message_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE,
    FOREIGN KEY (thread_id) REFERENCES mail_thread(thread_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX mail_message_by_external_id
    ON mail_message (account_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX mail_message_by_thread_received_at
    ON mail_message (thread_id, received_at DESC);
CREATE INDEX mail_message_by_received_at
    ON mail_message (account_id, received_at DESC);
CREATE INDEX mail_message_by_state
    ON mail_message (account_id, is_unread, is_starred, has_attachments);

CREATE TABLE mail_message_mailbox(
    message_id text NOT NULL,
    mailbox_id text NOT NULL,
    PRIMARY KEY (message_id, mailbox_id),
    FOREIGN KEY (message_id) REFERENCES mail_message(message_id) ON DELETE CASCADE,
    FOREIGN KEY (mailbox_id) REFERENCES mailbox(mailbox_id) ON DELETE CASCADE
) STRICT;

CREATE INDEX mail_message_mailbox_by_mailbox
    ON mail_message_mailbox (mailbox_id, message_id);

CREATE TABLE mail_attachment(
    message_id text NOT NULL,
    attachment_id text NOT NULL UNIQUE,
    external_id text,
    file_name text,
    mime_type text,
    size int NOT NULL DEFAULT 0,
    is_inline int NOT NULL DEFAULT 0,
    content_id text,
    content_path text,
    downloaded_at int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (message_id, attachment_id),
    FOREIGN KEY (message_id) REFERENCES mail_message(message_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX mail_attachment_by_external_id
    ON mail_attachment (message_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX mail_attachment_by_message
    ON mail_attachment (message_id);
