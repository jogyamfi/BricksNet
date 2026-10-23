-- Removes the live-test fixtures. The EF Core functional suite drops its own efct_* schemas, but
-- a run that was killed part-way can leave one behind; the second query lists any leftovers.

DROP SCHEMA IF EXISTS workspace.bricksnet_live CASCADE;

SHOW SCHEMAS IN workspace LIKE 'efct_*';
-- For each leftover:  DROP SCHEMA workspace.<name> CASCADE;
