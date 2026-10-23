-- Sanity checks after 01_setup.sql. Run in the SQL editor; every row should show ok = true.

USE CATALOG workspace;
USE SCHEMA bricksnet_live;

SELECT 'people rows'        AS item,  count(*) AS actual, 5       AS expected, count(*) = 5       AS ok FROM people
UNION ALL
SELECT 'active_people rows',          count(*),           3,                   count(*) = 3             FROM active_people
UNION ALL
SELECT 'all_types rows',              count(*),           3,                   count(*) = 3             FROM all_types
UNION ALL
SELECT 'big_table rows',              count(*),           2000000,             count(*) = 2000000       FROM big_table
UNION ALL
SELECT 'big_table sum(id)',           sum(id),            1999999000000,       sum(id) = 1999999000000  FROM big_table
UNION ALL
SELECT 'dml_target exists',           count(*),           count(*),            true                     FROM dml_target;

-- Confirms the identity the token runs as and the current catalog/schema.
SELECT current_user() AS user, current_catalog() AS catalog, current_schema() AS schema;
