-- PostgreSQL Database Initialization Script
-- Note: Make sure you are connected to the 'sql-encryption' database before running this script

-- Create schemas
CREATE SCHEMA IF NOT EXISTS gstusr;
CREATE SCHEMA IF NOT EXISTS usr;
CREATE SCHEMA IF NOT EXISTS supusr;

-- Create views
CREATE OR REPLACE VIEW gstusr."vwInfoDb" AS
    SELECT (SELECT COUNT(*) FROM supusr."Zoo") as "NrZoo",
        (SELECT COUNT(*) FROM supusr."Animals") as "NrAnimals";

-- Create the DeleteAll function (PostgreSQL uses functions instead of procedures for this pattern)
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


-- User and role management in PostgreSQL
-- Create roles (PostgreSQL roles are both users and groups)
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

-- Grant database connection privileges
GRANT CONNECT ON DATABASE "db-57875114" TO gstusr;
GRANT CONNECT ON DATABASE "db-57875114" TO usr;
GRANT CONNECT ON DATABASE "db-57875114" TO supusr;
GRANT CONNECT ON DATABASE "db-57875114" TO dbo;

-- Grant schema usage privileges
GRANT USAGE ON SCHEMA gstusr TO gstusrrole;
GRANT USAGE ON SCHEMA supusr TO gstusrrole;
GRANT USAGE ON SCHEMA public TO gstusrrole;

-- Grant role privileges for gstusrrole
GRANT SELECT ON gstusr."vwInfoDb" TO gstusrrole;

-- Grant role privileges for usrrole
GRANT USAGE ON SCHEMA supusr TO usrrole;
GRANT SELECT, UPDATE, INSERT ON ALL TABLES IN SCHEMA supusr TO usrrole;

-- Grant role privileges for supusrrole (inherit from usrrole)
GRANT DELETE ON ALL TABLES IN SCHEMA supusr TO supusrrole;
GRANT EXECUTE ON FUNCTION supusr."spDeleteAll"() TO supusrrole;

-- Grant role privileges for dborole (full privileges)
GRANT ALL PRIVILEGES ON DATABASE "db-57875114" TO dborole;
-- Grant superuser-like privileges (alternative: ALTER ROLE dborole SUPERUSER;)
GRANT CREATE ON DATABASE "db-57875114" TO dborole;
GRANT ALL ON ALL TABLES IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT ALL ON ALL SEQUENCES IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT ALL ON ALL FUNCTIONS IN SCHEMA gstusr, usr, supusr, public TO dborole;
GRANT USAGE, CREATE ON SCHEMA gstusr, usr, supusr, public TO dborole;

-- Assign users to roles
GRANT gstusrrole TO gstusr;

GRANT gstusrrole TO usr;
GRANT usrrole TO usr;

GRANT gstusrrole TO supusr;
GRANT usrrole TO supusr;
GRANT supusrrole TO supusr;

GRANT dborole TO dbo;

