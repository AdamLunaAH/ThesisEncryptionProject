-- PostgreSQL Database Initialization Script - COMPLETE
-- This script combines both application initialization and Orleans clustering setup
-- Execution order ensures all schemas, roles, and objects are created in the correct sequence
-- Note: Make sure you are connected to the 'sql-encryption' database before running this script

-- ============================================================================
-- PHASE 1: Create Schemas
-- ============================================================================
CREATE SCHEMA IF NOT EXISTS gstusr;
CREATE SCHEMA IF NOT EXISTS usr;
CREATE SCHEMA IF NOT EXISTS supusr;
CREATE SCHEMA IF NOT EXISTS benchmark;

-- ============================================================================
-- PHASE 2: Create Roles and Group Roles
-- ============================================================================
-- Create login roles
DO $BODY$
BEGIN
    -- Create login roles
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'gstusr') THEN
        CREATE ROLE gstusr WITH LOGIN PASSWORD 'pa$Word1';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'usr') THEN
        CREATE ROLE usr WITH LOGIN PASSWORD 'pa$Word1';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'supusr') THEN
        CREATE ROLE supusr WITH LOGIN PASSWORD 'pa$Word1';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'dbo') THEN
        CREATE ROLE dbo WITH LOGIN PASSWORD 'pa$Word1';
    END IF;

    -- Create group roles (note: lowercase names to match PostgreSQL convention)
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'gstusrrole') THEN
        CREATE ROLE gstusrrole;
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'usrrole') THEN
        CREATE ROLE usrrole;
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'supusrrole') THEN
        CREATE ROLE supusrrole;
    END IF;

    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'dborole') THEN
        CREATE ROLE dborole;
    END IF;
END
$BODY$;

-- ============================================================================
-- PHASE 3: Grant Database Connection Privileges
-- ============================================================================
GRANT CONNECT ON DATABASE "sql-encryption" TO gstusr;
GRANT CONNECT ON DATABASE "sql-encryption" TO usr;
GRANT CONNECT ON DATABASE "sql-encryption" TO supusr;
GRANT CONNECT ON DATABASE "sql-encryption" TO dbo;

-- ============================================================================
-- PHASE 4: Grant Schema Usage Privileges
-- ============================================================================
GRANT USAGE ON SCHEMA gstusr TO gstusrrole;
GRANT USAGE ON SCHEMA supusr TO gstusrrole;
GRANT USAGE ON SCHEMA public TO gstusrrole;
GRANT USAGE ON SCHEMA usr TO usrrole;
GRANT USAGE ON SCHEMA supusr TO usrrole;
GRANT USAGE ON SCHEMA gstusr TO supusrrole;
GRANT USAGE ON SCHEMA usr TO supusrrole;
GRANT USAGE ON SCHEMA supusr TO supusrrole;
GRANT USAGE, CREATE ON SCHEMA gstusr, usr, supusr, public TO dborole;

-- ============================================================================
-- PHASE 5: Create Application Views
-- ============================================================================

-- ============================================================================
-- PHASE 6: Create Application Functions
-- ============================================================================


-- ============================================================================
-- PHASE 6b: Create Benchmark Schema Tables
-- These tables mirror the EF Core migration output so the script can
-- initialise a blank database without requiring a separate migration run.
-- ============================================================================

-- Parent run record: one row per Execute() call / algorithm combination
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkRun"
(
    "BenchmarkRunId"   uuid                        NOT NULL,
    "RunAt"            timestamp with time zone    NOT NULL,
    "AlgorithmId"      varchar(200)                NOT NULL,
    "AlgorithmFamily"  varchar(200)                NOT NULL,
    "Generation"       varchar(200)                NOT NULL,
    "AuthId"           varchar(200)                    NULL,
    "MessageCount"     integer                     NOT NULL,
    "MessageSizeBytes" integer                     NOT NULL,
    "WarmupCount"      integer                     NOT NULL,
    "Notes"            varchar(200)                    NULL,

    CONSTRAINT "PK_BenchmarkRun" PRIMARY KEY ("BenchmarkRunId")
);

-- Per-message timing, size and memory metrics
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkMessageResult"
(
    "BenchmarkMessageResultId"   uuid             NOT NULL,
    "BenchmarkRunId"             uuid             NOT NULL,
    "MessageIndex"               integer          NOT NULL,
    "EncryptMicroseconds"        double precision NOT NULL,
    "DecryptMicroseconds"        double precision NOT NULL,
    "SignMicroseconds"           double precision NOT NULL,
    "VerifyMicroseconds"         double precision NOT NULL,
    "SignalRTransitMicroseconds" double precision NOT NULL DEFAULT 0,
    "TotalRoundTripMicroseconds" double precision NOT NULL,
    "PlaintextBytes"             integer          NOT NULL,
    "CiphertextBytes"            integer          NOT NULL,
    "EncapsulatedKeyBytes"       integer          NOT NULL,
    "MacTagBytes"                integer          NOT NULL,
    "TotalWireBytes"             integer          NOT NULL,
    "EncryptionOverheadBytes"    integer          NOT NULL,
    "GcAllocatedBytes"           bigint           NOT NULL,
    "GcGen0Collections"          integer          NOT NULL,
    "DecryptSuccess"             boolean          NOT NULL,
    "VerifySuccess"              boolean          NOT NULL,

    CONSTRAINT "PK_BenchmarkMessageResult" PRIMARY KEY ("BenchmarkMessageResultId"),
    CONSTRAINT "FK_BenchmarkMessageResult_BenchmarkRun_BenchmarkRunId"
        FOREIGN KEY ("BenchmarkRunId")
        REFERENCES benchmark."BenchmarkRun" ("BenchmarkRunId")
        ON DELETE CASCADE
);

-- Aggregate statistics computed over all measured messages in a run
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkSessionAggregate"
(
    "BenchmarkSessionAggregateId"      uuid             NOT NULL,
    "BenchmarkRunId"                   uuid             NOT NULL,
    "AvgEncryptMicroseconds"           double precision NOT NULL,
    "AvgDecryptMicroseconds"           double precision NOT NULL,
    "AvgSignMicroseconds"              double precision NOT NULL,
    "AvgVerifyMicroseconds"            double precision NOT NULL,
    "AvgSignalRTransitMicroseconds"    double precision NOT NULL DEFAULT 0,
    "AvgTotalRoundTripMicroseconds"    double precision NOT NULL,
    "MinRoundTripMicroseconds"         double precision NOT NULL,
    "MaxRoundTripMicroseconds"         double precision NOT NULL,
    "P50RoundTripMicroseconds"         double precision NOT NULL,
    "P95RoundTripMicroseconds"         double precision NOT NULL,
    "P99RoundTripMicroseconds"         double precision NOT NULL,
    "AvgCiphertextBytes"               double precision NOT NULL,
    "AvgEncapsulatedKeyBytes"          double precision NOT NULL,
    "AvgTotalWireBytes"                double precision NOT NULL,
    "AvgEncryptionOverheadBytes"       double precision NOT NULL,
    "TotalGcAllocatedBytes"            bigint           NOT NULL,
    "AvgGcAllocatedBytesPerMessage"    double precision NOT NULL,
    "TotalGcGen0Collections"           integer          NOT NULL,
    "SuccessfulMessages"               integer          NOT NULL,
    "SuccessRate"                      double precision NOT NULL,

    CONSTRAINT "PK_BenchmarkSessionAggregate" PRIMARY KEY ("BenchmarkSessionAggregateId"),
    CONSTRAINT "FK_BenchmarkSessionAggregate_BenchmarkRun_BenchmarkRunId"
        FOREIGN KEY ("BenchmarkRunId")
        REFERENCES benchmark."BenchmarkRun" ("BenchmarkRunId")
        ON DELETE CASCADE
);

-- Encrypted payload bytes captured per message (opt-in, separate from metrics table)
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkMessagePayload"
(
    "BenchmarkMessagePayloadId" uuid         NOT NULL,
    "BenchmarkRunId"            uuid         NOT NULL,
    "MessageIndex"              integer      NOT NULL,
    "AlgorithmId"               varchar(200) NOT NULL,
    "Ciphertext"                bytea        NOT NULL,
    "EncapsulatedKey"           bytea            NULL,
    "MacTag"                    bytea            NULL,
    "PlaintextBytes"            integer      NOT NULL,
    "TotalWireBytes"            integer      NOT NULL,
    "FilePath"                  varchar(200)     NULL,

    CONSTRAINT "PK_BenchmarkMessagePayload" PRIMARY KEY ("BenchmarkMessagePayloadId"),
    CONSTRAINT "FK_BenchmarkMessagePayload_BenchmarkRun_BenchmarkRunId"
        FOREIGN KEY ("BenchmarkRunId")
        REFERENCES benchmark."BenchmarkRun" ("BenchmarkRunId")
        ON DELETE CASCADE
);

-- Per-tick resource-monitor snapshots captured while a run executes
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkResourceSample"
(
    "BenchmarkResourceSampleId" uuid                     NOT NULL,
    "BenchmarkRunId"            uuid                     NOT NULL,
    "CapturedAt"                timestamp with time zone NOT NULL,
    "CpuPercent"                double precision         NOT NULL,
    "MemoryUsedMb"              double precision         NOT NULL,
    "GcHeapMb"                  double precision         NOT NULL,
    "GcGen0Collections"         integer                  NOT NULL,
    "GcGen1Collections"         integer                  NOT NULL,
    "GcGen2Collections"         integer                  NOT NULL,
    "ThreadPoolWorkerThreads"   integer                  NOT NULL,

    CONSTRAINT "PK_BenchmarkResourceSample" PRIMARY KEY ("BenchmarkResourceSampleId"),
    CONSTRAINT "FK_BenchmarkResourceSample_BenchmarkRun_BenchmarkRunId"
        FOREIGN KEY ("BenchmarkRunId")
        REFERENCES benchmark."BenchmarkRun" ("BenchmarkRunId")
        ON DELETE CASCADE
);

-- Pre-computed aggregate statistics derived from BenchmarkResourceSample rows
CREATE TABLE IF NOT EXISTS benchmark."BenchmarkResourceAggregate"
(
    "BenchmarkResourceAggregateId" uuid             NOT NULL,
    "BenchmarkRunId"               uuid             NOT NULL,
    "SampleCount"                  integer          NOT NULL,
    "DurationMilliseconds"          double precision NOT NULL,
    "CpuMax"                       double precision NOT NULL,
    "CpuAvg"                       double precision NOT NULL,
    "CpuP50"                       double precision NOT NULL,
    "CpuP95"                       double precision NOT NULL,
    "CpuP99"                       double precision NOT NULL,
    "MemoryMaxMb"                  double precision NOT NULL,
    "MemoryAvgMb"                  double precision NOT NULL,
    "MemoryP50Mb"                  double precision NOT NULL,
    "MemoryP95Mb"                  double precision NOT NULL,
    "MemoryP99Mb"                  double precision NOT NULL,
    "GcGen0Delta"                  integer          NOT NULL,
    "GcGen1Delta"                  integer          NOT NULL,
    "GcGen2Delta"                  integer          NOT NULL,
    "ThreadPoolMaxWorkers"         integer          NOT NULL,

    CONSTRAINT "PK_BenchmarkResourceAggregate" PRIMARY KEY ("BenchmarkResourceAggregateId"),
    CONSTRAINT "FK_BenchmarkResourceAggregate_BenchmarkRun_BenchmarkRunId"
        FOREIGN KEY ("BenchmarkRunId")
        REFERENCES benchmark."BenchmarkRun" ("BenchmarkRunId")
        ON DELETE CASCADE
);

-- Indexes mirroring the EF Core migration
CREATE INDEX IF NOT EXISTS "IX_BenchmarkMessageResult_BenchmarkRunId"
    ON benchmark."BenchmarkMessageResult" ("BenchmarkRunId");

CREATE INDEX IF NOT EXISTS "IX_BenchmarkSessionAggregate_BenchmarkRunId"
    ON benchmark."BenchmarkSessionAggregate" ("BenchmarkRunId");

CREATE INDEX IF NOT EXISTS "IX_BenchmarkMessagePayload_BenchmarkRunId"
    ON benchmark."BenchmarkMessagePayload" ("BenchmarkRunId");

CREATE INDEX IF NOT EXISTS "IX_BenchmarkMessagePayload_AlgorithmId"
    ON benchmark."BenchmarkMessagePayload" ("AlgorithmId");

CREATE INDEX IF NOT EXISTS "IX_BenchmarkResourceSample_BenchmarkRunId"
    ON benchmark."BenchmarkResourceSample" ("BenchmarkRunId");

CREATE INDEX IF NOT EXISTS "IX_BenchmarkResourceAggregate_BenchmarkRunId"
    ON benchmark."BenchmarkResourceAggregate" ("BenchmarkRunId");


-- ============================================================================
-- PHASE 7: Create Orleans Clustering Tables (in gstusr schema)
-- ============================================================================
-- Create the Orleans query storage table in gstusr schema
CREATE TABLE IF NOT EXISTS gstusr.OrleansQuery
(
    QueryKey varchar(64) NOT NULL,
    QueryText varchar(8000) NOT NULL,

    CONSTRAINT PK_OrleansQuery_QueryKey PRIMARY KEY(QueryKey)
);

-- For each deployment, there will be only one (active) membership version table version column which will be updated periodically.
CREATE TABLE IF NOT EXISTS gstusr.OrleansMembershipVersionTable
(
    DeploymentId varchar(150) NOT NULL,
    Timestamp timestamptz(3) NOT NULL DEFAULT now(),
    Version integer NOT NULL DEFAULT 0,

    CONSTRAINT PK_OrleansMembershipVersionTable_DeploymentId PRIMARY KEY(DeploymentId)
);

-- Every silo instance has a row in the membership table.
CREATE TABLE IF NOT EXISTS gstusr.OrleansMembershipTable
(
    DeploymentId varchar(150) NOT NULL,
    Address varchar(45) NOT NULL,
    Port integer NOT NULL,
    Generation integer NOT NULL,
    SiloName varchar(150) NOT NULL,
    HostName varchar(150) NOT NULL,
    Status integer NOT NULL,
    ProxyPort integer NULL,
    SuspectTimes varchar(8000) NULL,
    StartTime timestamptz(3) NOT NULL,
    IAmAliveTime timestamptz(3) NOT NULL,

    CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
    CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES gstusr.OrleansMembershipVersionTable (DeploymentId)
);

-- ============================================================================
-- PHASE 8: Create Orleans Clustering Functions (in gstusr schema)
-- ============================================================================
-- Create function to update IAmAliveTime
CREATE OR REPLACE FUNCTION gstusr.update_i_am_alive_time(
    deployment_id gstusr.OrleansMembershipTable.DeploymentId%TYPE,
    address_arg gstusr.OrleansMembershipTable.Address%TYPE,
    port_arg gstusr.OrleansMembershipTable.Port%TYPE,
    generation_arg gstusr.OrleansMembershipTable.Generation%TYPE,
    i_am_alive_time gstusr.OrleansMembershipTable.IAmAliveTime%TYPE)
  RETURNS void AS
$func$
BEGIN
    -- This is expected to never fail by Orleans, so return value
    -- is not needed nor is it checked.
    UPDATE gstusr.OrleansMembershipTable as d
    SET
        IAmAliveTime = i_am_alive_time
    WHERE
        d.DeploymentId = deployment_id AND deployment_id IS NOT NULL
        AND d.Address = address_arg AND address_arg IS NOT NULL
        AND d.Port = port_arg AND port_arg IS NOT NULL
        AND d.Generation = generation_arg AND generation_arg IS NOT NULL;
END
$func$ LANGUAGE plpgsql;

-- Create function to insert membership version
CREATE OR REPLACE FUNCTION gstusr.insert_membership_version(
    DeploymentIdArg gstusr.OrleansMembershipTable.DeploymentId%TYPE
)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN

    BEGIN

        INSERT INTO gstusr.OrleansMembershipVersionTable
        (
            DeploymentId
        )
        SELECT DeploymentIdArg
        ON CONFLICT (DeploymentId) DO NOTHING;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        ASSERT RowCountVar <> 0, 'no rows affected, rollback';

        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;

END
$func$ LANGUAGE plpgsql;

-- Create function to insert membership
CREATE OR REPLACE FUNCTION gstusr.insert_membership(
    DeploymentIdArg gstusr.OrleansMembershipTable.DeploymentId%TYPE,
    AddressArg      gstusr.OrleansMembershipTable.Address%TYPE,
    PortArg         gstusr.OrleansMembershipTable.Port%TYPE,
    GenerationArg   gstusr.OrleansMembershipTable.Generation%TYPE,
    SiloNameArg     gstusr.OrleansMembershipTable.SiloName%TYPE,
    HostNameArg     gstusr.OrleansMembershipTable.HostName%TYPE,
    StatusArg       gstusr.OrleansMembershipTable.Status%TYPE,
    ProxyPortArg    gstusr.OrleansMembershipTable.ProxyPort%TYPE,
    StartTimeArg    gstusr.OrleansMembershipTable.StartTime%TYPE,
    IAmAliveTimeArg gstusr.OrleansMembershipTable.IAmAliveTime%TYPE,
    VersionArg      gstusr.OrleansMembershipVersionTable.Version%TYPE)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN

    BEGIN
        INSERT INTO gstusr.OrleansMembershipTable
        (
            DeploymentId,
            Address,
            Port,
            Generation,
            SiloName,
            HostName,
            Status,
            ProxyPort,
            StartTime,
            IAmAliveTime
        )
        SELECT
            DeploymentIdArg,
            AddressArg,
            PortArg,
            GenerationArg,
            SiloNameArg,
            HostNameArg,
            StatusArg,
            ProxyPortArg,
            StartTimeArg,
            IAmAliveTimeArg
        ON CONFLICT (DeploymentId, Address, Port, Generation) DO
            NOTHING;


        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        UPDATE gstusr.OrleansMembershipVersionTable
        SET
            Timestamp = now(),
            Version = Version + 1
        WHERE
            DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Version = VersionArg AND VersionArg IS NOT NULL
            AND RowCountVar > 0;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        ASSERT RowCountVar <> 0, 'no rows affected, rollback';


        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;

END
$func$ LANGUAGE plpgsql;

-- Create function to update membership
CREATE OR REPLACE FUNCTION gstusr.update_membership(
    DeploymentIdArg gstusr.OrleansMembershipTable.DeploymentId%TYPE,
    AddressArg      gstusr.OrleansMembershipTable.Address%TYPE,
    PortArg         gstusr.OrleansMembershipTable.Port%TYPE,
    GenerationArg   gstusr.OrleansMembershipTable.Generation%TYPE,
    StatusArg       gstusr.OrleansMembershipTable.Status%TYPE,
    SuspectTimesArg gstusr.OrleansMembershipTable.SuspectTimes%TYPE,
    IAmAliveTimeArg gstusr.OrleansMembershipTable.IAmAliveTime%TYPE,
    VersionArg      gstusr.OrleansMembershipVersionTable.Version%TYPE
  )
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN

    BEGIN

    UPDATE gstusr.OrleansMembershipVersionTable
    SET
        Timestamp = now(),
        Version = Version + 1
    WHERE
        DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
        AND Version = VersionArg AND VersionArg IS NOT NULL;


    GET DIAGNOSTICS RowCountVar = ROW_COUNT;

    UPDATE gstusr.OrleansMembershipTable
    SET
        Status = StatusArg,
        SuspectTimes = SuspectTimesArg,
        IAmAliveTime = IAmAliveTimeArg
    WHERE
        DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
        AND Address = AddressArg AND AddressArg IS NOT NULL
        AND Port = PortArg AND PortArg IS NOT NULL
        AND Generation = GenerationArg AND GenerationArg IS NOT NULL
        AND RowCountVar > 0;


        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        ASSERT RowCountVar <> 0, 'no rows affected, rollback';


        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;

END
$func$ LANGUAGE plpgsql;

-- ============================================================================
-- PHASE 9: Populate Orleans Query Storage Table
-- ============================================================================
-- Insert UpdateIAmAliveTime query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'UpdateIAmAlivetimeKey','
    -- This is expected to never fail by Orleans, so return value
    -- is not needed nor is it checked.
    SELECT * from gstusr.update_i_am_alive_time(
        @DeploymentId,
        @Address,
        @Port,
        @Generation,
        @IAmAliveTime
    );
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert InsertMembershipVersion query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'InsertMembershipVersionKey','
    SELECT * FROM gstusr.insert_membership_version(
        @DeploymentId
    );
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert InsertMembership query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'InsertMembershipKey','
    SELECT * FROM gstusr.insert_membership(
        @DeploymentId,
        @Address,
        @Port,
        @Generation,
        @SiloName,
        @HostName,
        @Status,
        @ProxyPort,
        @StartTime,
        @IAmAliveTime,
        @Version
    );
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert UpdateMembership query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'UpdateMembershipKey','
    SELECT * FROM gstusr.update_membership(
        @DeploymentId,
        @Address,
        @Port,
        @Generation,
        @Status,
        @SuspectTimes,
        @IAmAliveTime,
        @Version
    );
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert MembershipReadRow query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'MembershipReadRowKey','
    SELECT
        v.DeploymentId,
        m.Address,
        m.Port,
        m.Generation,
        m.SiloName,
        m.HostName,
        m.Status,
        m.ProxyPort,
        m.SuspectTimes,
        m.StartTime,
        m.IAmAliveTime,
        v.Version
    FROM
        gstusr.OrleansMembershipVersionTable v
        -- This ensures the version table will returned even if there is no matching membership row.
        LEFT OUTER JOIN gstusr.OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
        AND Address = @Address AND @Address IS NOT NULL
        AND Port = @Port AND @Port IS NOT NULL
        AND Generation = @Generation AND @Generation IS NOT NULL
    WHERE
        v.DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert MembershipReadAll query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'MembershipReadAllKey','
    SELECT
        v.DeploymentId,
        m.Address,
        m.Port,
        m.Generation,
        m.SiloName,
        m.HostName,
        m.Status,
        m.ProxyPort,
        m.SuspectTimes,
        m.StartTime,
        m.IAmAliveTime,
        v.Version
    FROM
        gstusr.OrleansMembershipVersionTable v LEFT OUTER JOIN gstusr.OrleansMembershipTable m
        ON v.DeploymentId = m.DeploymentId
    WHERE
        v.DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert DeleteMembershipTableEntries query
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'DeleteMembershipTableEntriesKey','
    DELETE FROM gstusr.OrleansMembershipTable
    WHERE DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;
    DELETE FROM gstusr.OrleansMembershipVersionTable
    WHERE DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert GatewaysQuery
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'GatewaysQueryKey','
    SELECT
        Address,
        ProxyPort,
        Generation
    FROM
        gstusr.OrleansMembershipTable
    WHERE
        DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL
        AND Status = @Status AND @Status IS NOT NULL
        AND ProxyPort > 0;
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Insert CleanupDefunctSiloEntries query (required for Orleans 10.1.0)
INSERT INTO gstusr.OrleansQuery(QueryKey, QueryText)
VALUES
(
    'CleanupDefunctSiloEntriesKey','
    DELETE FROM gstusr.OrleansMembershipTable
    WHERE
        DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL
        AND IAmAliveTime < now() - INTERVAL ''@SiloDeactivationThreshold ms'';
')
ON CONFLICT (QueryKey) DO NOTHING;

-- ============================================================================
-- PHASE 10: Grant Role Permissions (Application Objects)
-- ============================================================================
-- Grant role privileges for gstusrrole (view access)

-- Grant role privileges for usrrole (table operations)
GRANT SELECT, UPDATE, INSERT ON ALL TABLES IN SCHEMA supusr TO usrrole;

-- Grant role privileges for supusrrole (delete operations)
GRANT DELETE ON ALL TABLES IN SCHEMA supusr TO supusrrole;

-- Grant schema usage on benchmark to dborole (dbo user writes benchmark results)
GRANT USAGE ON SCHEMA benchmark TO dborole;
GRANT ALL ON ALL TABLES IN SCHEMA benchmark TO dborole;
-- Ensure any tables added by future EF migrations are also accessible to dborole
-- without needing a separate GRANT run after each migration.
ALTER DEFAULT PRIVILEGES IN SCHEMA benchmark GRANT ALL ON TABLES TO dborole;

-- ============================================================================
-- PHASE 11: Grant Role Permissions (Orleans Objects)
-- ============================================================================
-- Grant table permissions to gstusr role (via gstusrrole group)
GRANT SELECT, INSERT, UPDATE, DELETE ON gstusr.OrleansQuery TO gstusrrole;
GRANT SELECT, INSERT, UPDATE, DELETE ON gstusr.OrleansMembershipVersionTable TO gstusrrole;
GRANT SELECT, INSERT, UPDATE, DELETE ON gstusr.OrleansMembershipTable TO gstusrrole;

-- Grant function permissions to gstusr role (via gstusrrole group)
GRANT EXECUTE ON FUNCTION gstusr.update_i_am_alive_time(varchar, varchar, integer, integer, timestamptz) TO gstusrrole;
GRANT EXECUTE ON FUNCTION gstusr.insert_membership_version(varchar) TO gstusrrole;
GRANT EXECUTE ON FUNCTION gstusr.insert_membership(varchar, varchar, integer, integer, varchar, varchar, integer, integer, timestamptz, timestamptz, integer) TO gstusrrole;
GRANT EXECUTE ON FUNCTION gstusr.update_membership(varchar, varchar, integer, integer, integer, varchar, timestamptz, integer) TO gstusrrole;

-- ============================================================================
-- PHASE 12: Grant Role Assignments
-- ============================================================================
-- Assign users to roles
GRANT gstusrrole TO gstusr;

GRANT gstusrrole TO usr;
GRANT usrrole TO usr;

GRANT gstusrrole TO supusr;
GRANT usrrole TO supusr;
GRANT supusrrole TO supusr;

GRANT dborole TO dbo;

-- ============================================================================
-- PHASE 13: Grant Full DBO Privileges
-- ============================================================================
-- Grant superuser-like privileges
GRANT CREATE ON DATABASE "sql-encryption" TO dborole;
GRANT ALL ON ALL TABLES IN SCHEMA gstusr, usr, supusr, public, benchmark TO dborole;
GRANT ALL ON ALL SEQUENCES IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT ALL ON ALL FUNCTIONS IN SCHEMA gstusr, usr, supusr, public TO dborole;

-- ============================================================================
-- PHASE 14: Verification and Summary
-- ============================================================================
SELECT 'Database initialization complete!' AS status;
SELECT 'Application schemas, roles, and Orleans clustering infrastructure ready.' AS description;

SELECT COUNT(*) as application_views FROM information_schema.views WHERE table_schema = 'gstusr';
SELECT COUNT(*) as application_functions FROM pg_proc WHERE pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'supusr');
SELECT COUNT(*) as orleans_tables FROM information_schema.tables WHERE table_schema = 'gstusr' AND table_name LIKE 'Orleans%';
SELECT COUNT(*) as orleans_functions FROM pg_proc WHERE pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'gstusr') AND proname LIKE 'insert_%' OR proname LIKE 'update_%';
SELECT COUNT(*) as orleans_queries FROM gstusr.OrleansQuery;
