-- Address books (for CardDAV accounts and Google contacts)
CREATE TABLE address_book(
    account_id text NOT NULL,
    address_book_id text NOT NULL UNIQUE,
    external_id text,
    name text NOT NULL,
    enabled int NOT NULL DEFAULT 1,
    last_sync int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, address_book_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX address_book_by_external_id 
    ON address_book (account_id, external_id) WHERE external_id IS NOT NULL;

-- Contacts
CREATE TABLE contact(
    address_book_id text NOT NULL,
    contact_id text NOT NULL UNIQUE,
    external_id text,
    display_name text,
    given_name text,
    family_name text,
    primary_email text,
    primary_phone text,
    photo_url text,
    changed_at int,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (address_book_id, contact_id),
    FOREIGN KEY (address_book_id) REFERENCES address_book(address_book_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX contact_by_external_id 
    ON contact (address_book_id, external_id) WHERE external_id IS NOT NULL;
CREATE INDEX contact_by_name ON contact (display_name COLLATE NOCASE);
CREATE INDEX contact_by_email ON contact (primary_email COLLATE NOCASE);

-- Contact groups/labels
CREATE TABLE contact_group(
    account_id text NOT NULL,
    group_id text NOT NULL UNIQUE,
    external_id text,
    name text NOT NULL,
    system_group int NOT NULL DEFAULT 0,
    data blob CHECK ( json_valid(data, 8) ),

    PRIMARY KEY (account_id, group_id),
    FOREIGN KEY (account_id) REFERENCES account(account_id) ON DELETE CASCADE
) STRICT;

CREATE UNIQUE INDEX contact_group_by_external_id 
    ON contact_group (account_id, external_id) WHERE external_id IS NOT NULL;

-- Contact-to-group membership
CREATE TABLE contact_group_membership(
    contact_id text NOT NULL,
    group_id text NOT NULL,
    PRIMARY KEY (contact_id, group_id),
    FOREIGN KEY (contact_id) REFERENCES contact(contact_id) ON DELETE CASCADE,
    FOREIGN KEY (group_id) REFERENCES contact_group(group_id) ON DELETE CASCADE
) STRICT;

CREATE INDEX contact_group_membership_by_group ON contact_group_membership (group_id);
