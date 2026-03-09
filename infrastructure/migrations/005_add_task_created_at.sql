-- Add created_at to tasks if it doesn't already exist
ALTER TABLE tasks ADD COLUMN IF NOT EXISTS created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;

-- Backfill: for tasks whose earliest progress/note record predates their created_at,
-- set created_at to that earliest date (handles tasks inserted before this column existed)
UPDATE tasks t
SET created_at = earliest.min_date::timestamp
FROM (
    SELECT task_id, MIN(log_date)::timestamp AS min_date
    FROM (
        SELECT task_id, log_date FROM progress
        UNION ALL
        SELECT task_id, log_date FROM task_notes
    ) combined
    GROUP BY task_id
) earliest
WHERE t.id = earliest.task_id
  AND earliest.min_date < t.created_at::date;
