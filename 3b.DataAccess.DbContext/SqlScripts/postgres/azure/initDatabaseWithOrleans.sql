-- PostgreSQL Database Initialization Script - COMPLETE (Azure Version)
-- This script combines both application initialization and Orleans clustering setup
-- Execution order ensures all schemas, roles, and objects are created in the correct sequence
-- Note: Make sure you are connected to the 'db-57875114' database before running this script

-- ============================================================================
-- PHASE 1: Create Schemas
-- ============================================================================
CREATE SCHEMA IF NOT EXISTS gstusr;
CREATE SCHEMA IF NOT EXISTS usr;
CREATE SCHEMA IF NOT EXISTS supusr;

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
GRANT CONNECT ON DATABASE "db-57875114" TO gstusr;
GRANT CONNECT ON DATABASE "db-57875114" TO usr;
GRANT CONNECT ON DATABASE "db-57875114" TO supusr;
GRANT CONNECT ON DATABASE "db-57875114" TO dbo;

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
CREATE OR REPLACE VIEW gstusr."vwInfoDb" AS
    SELECT (SELECT COUNT(*) FROM supusr."Zoo") as "NrZoo",
        (SELECT COUNT(*) FROM supusr."Animals") as "NrAnimals";

-- ============================================================================
-- PHASE 6: Create Application Functions
-- ============================================================================
CREATE OR REPLACE FUNCTION supusr."spDeleteAll"()
RETURNS TABLE("NrZoo" BIGINT, "NrAnimals" BIGINT)
LANGUAGE plpgsql
AS $$
BEGIN

    DELETE FROM supusr."Zoo";
    DELETE FROM supusr."Animals";

    -- Test to throw an error (uncomment if needed)
    -- RAISE EXCEPTION 'Error occurred in supusr.spDeleteAll';

    RETURN QUERY SELECT * FROM gstusr."vwInfoDb";
END;
$$;

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
GRANT SELECT ON gstusr."vwInfoDb" TO gstusrrole;

-- Grant role privileges for usrrole (table operations)
GRANT SELECT, UPDATE, INSERT ON ALL TABLES IN SCHEMA supusr TO usrrole;

-- Grant role privileges for supusrrole (delete operations)
GRANT DELETE ON ALL TABLES IN SCHEMA supusr TO supusrrole;
GRANT EXECUTE ON FUNCTION supusr."spDeleteAll"() TO supusrrole;

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
GRANT CREATE ON DATABASE "db-57875114" TO dborole;
GRANT ALL ON ALL TABLES IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT ALL ON ALL SEQUENCES IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT ALL ON ALL FUNCTIONS IN SCHEMA gstusr, usr, supusr, public TO dborole;

-- ============================================================================
-- PHASE 14: Verification and Summary
-- ============================================================================
SELECT 'Azure Database initialization complete!' AS status;
SELECT 'Application schemas, roles, and Orleans clustering infrastructure ready for Azure deployment.' AS description;

SELECT COUNT(*) as application_views FROM information_schema.views WHERE table_schema = 'gstusr';
SELECT COUNT(*) as application_functions FROM pg_proc WHERE pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'supusr');
SELECT COUNT(*) as orleans_tables FROM information_schema.tables WHERE table_schema = 'gstusr' AND table_name LIKE 'Orleans%';
SELECT COUNT(*) as orleans_functions FROM pg_proc WHERE pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'gstusr') AND (proname LIKE 'insert_%' OR proname LIKE 'update_%');
SELECT COUNT(*) as orleans_queries FROM gstusr.OrleansQuery;
