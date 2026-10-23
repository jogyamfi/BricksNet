-- BricksNet live-test fixtures.
--
-- Run this once in the Databricks SQL editor (attached to your SQL warehouse) before running the
-- live tests. It is idempotent: re-running it rebuilds every table with the same data.
--
-- Defaults to the `workspace` catalog that Databricks Free Edition creates for you. If you use a
-- different catalog, change the USE CATALOG line and set DATABRICKS_CATALOG to match.
--
-- scripts/Run-LiveTests.ps1 -Steps Setup splits this file on semicolons, so keep them out of
-- string literals.

USE CATALOG workspace;

CREATE SCHEMA IF NOT EXISTS bricksnet_live
  COMMENT 'Fixtures for the BricksNet live (E2E) test suite. Safe to drop.';

USE SCHEMA bricksnet_live;

-- ---------------------------------------------------------------------------------------------
-- people: small table for basic SELECTs, filters, parameters, Dapper mapping and metadata calls.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE TABLE people (
  id         INT       NOT NULL,
  name       STRING,
  city       STRING,
  active     BOOLEAN   NOT NULL,
  score      DOUBLE,
  created_at TIMESTAMP NOT NULL
) COMMENT 'Five known rows. Row 5 has NULL name, city and score.';

INSERT INTO people VALUES
  (1, 'Ada',      'London',     true,  91.5, TIMESTAMP'2024-01-15 09:30:00'),
  (2, 'Brendan',  'Dublin',     true,  78.0, TIMESTAMP'2024-02-01 12:00:00'),
  (3, 'Chiamaka', 'Lagos',      false, 66.25, TIMESTAMP'2024-03-10 18:45:30'),
  (4, 'Dmitri',   'Tallinn',    true,  NULL, TIMESTAMP'2024-04-20 00:00:00'),
  (5, NULL,       NULL,         false, NULL, TIMESTAMP'2024-05-05 05:05:05');

CREATE OR REPLACE VIEW active_people AS
  SELECT id, name, city FROM people WHERE active;

-- ---------------------------------------------------------------------------------------------
-- all_types: one column per Databricks SQL type. Row 1 = typical values, row 2 = edge values,
-- row 3 = all NULLs (except id). Exercises the Arrow and JSON result converters.
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE TABLE all_types (
  id            INT NOT NULL,
  c_boolean     BOOLEAN,
  c_tinyint     TINYINT,
  c_smallint    SMALLINT,
  c_int         INT,
  c_bigint      BIGINT,
  c_float       FLOAT,
  c_double      DOUBLE,
  c_decimal     DECIMAL(18, 4),
  c_decimal_max DECIMAL(38, 0),
  c_string      STRING,
  c_binary      BINARY,
  c_date        DATE,
  c_timestamp   TIMESTAMP,
  c_timestamp_ntz TIMESTAMP_NTZ,
  c_array       ARRAY<INT>,
  c_map         MAP<STRING, INT>,
  c_struct      STRUCT<a: INT, b: STRING>
);

INSERT INTO all_types VALUES
  (1, true, 7, 300, 70000, 9000000000, 1.5, 2.25, 12345.6789,
      12345678901234567890123456789012345678,
      'hello, world', X'DEADBEEF', DATE'2024-02-29',
      TIMESTAMP'2024-02-29 13:14:15.123456', TIMESTAMP_NTZ'2024-02-29 13:14:15.123456',
      array(1, 2, 3), map('a', 1, 'b', 2), named_struct('a', 1, 'b', 'x')),
  (2, false, -128, -32768, -2147483648, -9223372036854775808, -3.4e38, 1.7976931348623157e308,
      -99999999999999.9999,
      -99999999999999999999999999999999999999,
      'unicode: é ü 中文 🚀 and \'quotes\'', X'', DATE'0001-01-01',
      TIMESTAMP'1970-01-01 00:00:00', TIMESTAMP_NTZ'9999-12-31 23:59:59.999999',
      array(), map(), named_struct('a', NULL, 'b', NULL)),
  (3, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
      NULL, NULL);

-- ---------------------------------------------------------------------------------------------
-- big_table: 2,000,000 rows, large enough that the warehouse returns several result chunks
-- (CloudFetch external links over REST, multiple fetches over Thrift).
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE TABLE big_table AS
SELECT
  id,
  CAST(id % 1000 AS INT)                       AS bucket,
  concat('row-', CAST(id AS STRING))           AS label,
  CAST(id AS DOUBLE) / 7                       AS ratio,
  date_add(DATE'2020-01-01', CAST(id % 1500 AS INT)) AS day
FROM range(0, 2000000);

-- ---------------------------------------------------------------------------------------------
-- dml_target: emptied and refilled by the DML tests (INSERT/UPDATE/DELETE/MERGE row counts).
-- ---------------------------------------------------------------------------------------------
CREATE OR REPLACE TABLE dml_target (
  id     INT NOT NULL,
  status STRING,
  amount DECIMAL(10, 2)
);
