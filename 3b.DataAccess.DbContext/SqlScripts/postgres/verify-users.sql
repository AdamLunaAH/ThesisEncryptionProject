-- PostgreSQL User Verification Script
-- Note: Make sure you are connected to the 'sql-encryption' database before running this script

-- Show all roles (users and groups in PostgreSQL)
SELECT rolname, rolcanlogin, rolsuper, rolinherit, rolcreaterole, rolcreatedb
FROM pg_catalog.pg_roles
WHERE rolname IN ('gstusr', 'usr', 'supusr', 'dbo', 'gstusrrole', 'usrrole', 'supusrrole', 'dborole')
ORDER BY rolname;

-- Show role memberships
SELECT
    r.rolname AS role,
    m.rolname AS member
FROM pg_catalog.pg_roles r
JOIN pg_catalog.pg_auth_members am ON r.oid = am.roleid
JOIN pg_catalog.pg_roles m ON am.member = m.oid
WHERE r.rolname IN ('gstusrrole', 'usrrole', 'supusrrole', 'dborole')
ORDER BY r.rolname, m.rolname;


-- Show effective privileges based on role memberships
WITH role_hierarchy AS (
  SELECT
    m.rolname AS user_role,
    r.rolname AS inherited_role
  FROM pg_catalog.pg_roles m
  JOIN pg_catalog.pg_auth_members am ON m.oid = am.member
  JOIN pg_catalog.pg_roles r ON am.roleid = r.oid
  WHERE m.rolname IN ('gstusr', 'usr', 'supusr', 'dbo')
)
SELECT DISTINCT
  rh.user_role,
  'schema' AS object_type,
  up.object_name AS object_name,
  up.privilege_type
FROM role_hierarchy rh
JOIN information_schema.usage_privileges up ON rh.inherited_role = up.grantee
JOIN pg_catalog.pg_namespace n ON n.nspname = up.object_name
WHERE up.object_name IN ('gstusr', 'usr', 'supusr', 'dbo', 'public')

UNION ALL

SELECT DISTINCT
  rh.user_role,
  'table' AS object_type,
  tp.table_schema || '.' || tp.table_name AS object_name,
  tp.privilege_type
FROM role_hierarchy rh
JOIN information_schema.table_privileges tp ON rh.inherited_role = tp.grantee
WHERE tp.table_schema IN ('gstusr', 'usr', 'supusr', 'dbo', 'public')

UNION ALL

SELECT DISTINCT
  rh.user_role,
  'function' AS object_type,
  rp.routine_schema || '.' || rp.routine_name AS object_name,
  rp.privilege_type
FROM role_hierarchy rh
JOIN information_schema.routine_privileges rp ON rh.inherited_role = rp.grantee
WHERE rp.routine_schema IN ('gstusr', 'usr', 'supusr', 'dbo')

ORDER BY user_role, object_type, object_name, privilege_type;
