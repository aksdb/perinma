CREATE TABLE mail_compose_draft(
    draft_id text NOT NULL UNIQUE,
    account_id text NOT NULL,
    compose_kind text NOT NULL,
    source_message_id text,
    source_message_external_id text,
    source_thread_id text,
    source_thread_external_id text,
    source_internet_message_id text,
    remote_draft_reference_json text,
    selected_identity_id text,
    selected_identity_display_name text,
    selected_identity_address text,
    subject text NOT NULL DEFAULT '',
    html_body text NOT NULL DEFAULT '',
    plain_text_body text NOT NULL DEFAULT '',
    status text NOT NULL DEFAULT 'LocalOnly',
    last_local_save_at int,
    last_remote_save_at int,
    updated_at int NOT NULL,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (draft_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE,
    FOREIGN KEY (source_message_id) REFERENCES mail_message(message_id) ON DELETE SET NULL,
    FOREIGN KEY (source_thread_id) REFERENCES mail_thread(thread_id) ON DELETE SET NULL
) STRICT;

CREATE INDEX mail_compose_draft_by_account_updated_at
    ON mail_compose_draft (account_id, updated_at DESC);
CREATE INDEX mail_compose_draft_by_status
    ON mail_compose_draft (status, updated_at DESC);

CREATE TABLE mail_compose_recipient(
    draft_id text NOT NULL,
    recipient_id text NOT NULL UNIQUE,
    recipient_kind text NOT NULL,
    display_name text,
    address text NOT NULL,
    sort_order int NOT NULL DEFAULT 0,

    PRIMARY KEY (draft_id, recipient_id),
    FOREIGN KEY (draft_id) REFERENCES mail_compose_draft(draft_id) ON DELETE CASCADE
) STRICT;

CREATE INDEX mail_compose_recipient_by_draft
    ON mail_compose_recipient (draft_id, recipient_kind, sort_order, recipient_id);

CREATE TABLE mail_compose_attachment(
    draft_id text NOT NULL,
    attachment_id text NOT NULL UNIQUE,
    file_name text NOT NULL,
    mime_type text NOT NULL,
    size int NOT NULL DEFAULT 0,
    is_inline int NOT NULL DEFAULT 0,
    content_id text,
    staged_file_path text NOT NULL,
    content_hash text,
    provider_attachment_reference_json text,
    sort_order int NOT NULL DEFAULT 0,

    PRIMARY KEY (draft_id, attachment_id),
    FOREIGN KEY (draft_id) REFERENCES mail_compose_draft(draft_id) ON DELETE CASCADE
) STRICT;

CREATE INDEX mail_compose_attachment_by_draft
    ON mail_compose_attachment (draft_id, sort_order, attachment_id);
CREATE INDEX mail_compose_attachment_by_hash
    ON mail_compose_attachment (draft_id, content_hash) WHERE content_hash IS NOT NULL;
