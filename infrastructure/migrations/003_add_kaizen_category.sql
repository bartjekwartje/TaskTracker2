INSERT INTO task_categories (category_name, description)
VALUES ('kaizen', 'Five-level progress indicator: dot (worked on it), plus (improved it), circle (finished it), circle-plus (finished & improved), diamond (something special)')
ON CONFLICT (category_name) DO NOTHING;
