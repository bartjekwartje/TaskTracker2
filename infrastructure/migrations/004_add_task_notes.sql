CREATE TABLE IF NOT EXISTS task_notes (
    task_id    VARCHAR(50) NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    log_date   DATE NOT NULL,
    note       TEXT NOT NULL DEFAULT '',
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (task_id, log_date)
);
