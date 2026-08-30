-- Backfill activity entries for expenses, settlements and comments that have none.
--
-- Activity is a log written when an action happens, so the fix that made the sync
-- path record it does nothing for rows written before. This writes the missing
-- entries from what the rows themselves say, dated by when they were created.
--
-- Idempotent: it only inserts where no entry already points at the row, so running
-- it twice adds nothing. Read-only until the final COMMIT, so the SELECT counts can
-- be checked first.
--
-- Kinds, from ActivityKind: 0 ExpenseCreated, 4 SettlementCreated, 6 CommentPosted.
-- Subject types, from SyncEntityType: 2 Expense, 5 Settlement, 6 ExpenseComment.
-- The subject type is what the app reads to decide an entry opens the expense, so
-- these numbers have to match the enum rather than look plausible.

BEGIN;

-- What is missing, before changing anything.
SELECT 'expenses without an activity entry' AS what, count(*) AS rows
FROM expenses e
WHERE NOT e.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = e.id AND a.kind = 0)
UNION ALL
SELECT 'settlements without one', count(*)
FROM settlements s
WHERE NOT s.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = s.id AND a.kind = 4)
UNION ALL
SELECT 'comments without one', count(*)
FROM expense_comments c
WHERE NOT c.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = c.id AND a.kind = 6);

-- Expenses. The payer is the actor: nothing records who typed it in, and the payer
-- is the person the entry is about.
INSERT INTO activity_log (
  group_id, kind, actor_user_id, actor_member_id, subject_type, subject_id,
  summary, occurred_at)
SELECT
  e.group_id,
  0,
  m.user_id,
  e.paid_by_member_id,
  2,
  e.id,
  left(
    coalesce(m.display_name, 'Someone') || ' added ' || e.description ||
    ' (' || to_char(e.amount, 'FM999999990.00') || ' ' || e.currency || ')',
    500),
  e.created_at
FROM expenses e
LEFT JOIN group_members m ON m.id = e.paid_by_member_id
WHERE NOT e.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = e.id AND a.kind = 0);

-- Settlements.
INSERT INTO activity_log (
  group_id, kind, actor_user_id, actor_member_id, subject_type, subject_id,
  summary, occurred_at)
SELECT
  s.group_id,
  4,
  payer.user_id,
  s.from_member_id,
  5,
  s.id,
  left(
    coalesce(payer.display_name, 'Someone') || ' paid ' ||
    coalesce(payee.display_name, 'someone') || ' ' ||
    to_char(s.amount, 'FM999999990.00') || ' ' || s.currency,
    500),
  s.created_at
FROM settlements s
LEFT JOIN group_members payer ON payer.id = s.from_member_id
LEFT JOIN group_members payee ON payee.id = s.to_member_id
WHERE NOT s.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = s.id AND a.kind = 4);

-- Comments.
INSERT INTO activity_log (
  group_id, kind, actor_user_id, actor_member_id, subject_type, subject_id,
  summary, occurred_at)
SELECT
  c.group_id,
  6,
  m.user_id,
  c.author_member_id,
  6,
  c.id,
  left(
    coalesce(m.display_name, 'Someone') || ' commented on ' ||
    coalesce(e.description, 'an expense'),
    500),
  c.created_at
FROM expense_comments c
LEFT JOIN group_members m ON m.id = c.author_member_id
LEFT JOIN expenses e ON e.id = c.expense_id
WHERE NOT c.is_deleted
  AND NOT EXISTS (SELECT 1 FROM activity_log a WHERE a.subject_id = c.id AND a.kind = 6);

-- What the feed holds afterwards.
SELECT kind, count(*) AS rows FROM activity_log GROUP BY kind ORDER BY kind;

COMMIT;
