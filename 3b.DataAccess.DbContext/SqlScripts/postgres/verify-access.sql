-- Connect to sql-encryption database
-- Note: Make sure you are connected to the 'sql-encryption' database before running this script

-- Set role to gstusr
SET ROLE gstusr;

DO $$
BEGIN
    RAISE NOTICE 'Testing access as gstusr';

    -- Test access to various objects
    PERFORM * FROM gstusr."vwInfoDb";
    RAISE NOTICE 'gstusr can access gstusr.vwInfoDb';

    -- Should fail
    BEGIN
        PERFORM * FROM supusr."MusicGroups" LIMIT 5;
        RAISE NOTICE 'ERROR: gstusr should not access supusr.MusicGroups';
    EXCEPTION
        WHEN insufficient_privilege THEN
            RAISE NOTICE 'EXPECTED: gstusr cannot access supusr.MusicGroups - %', SQLERRM;
        WHEN OTHERS THEN
            RAISE NOTICE 'gstusr access to supusr.MusicGroups failed with: %', SQLERRM;
    END;

    BEGIN
        PERFORM * FROM dbo."Users" LIMIT 5;
        RAISE NOTICE 'ERROR: gstusr should not access dbo.Users';
    EXCEPTION
        WHEN insufficient_privilege THEN
            RAISE NOTICE 'EXPECTED: gstusr cannot access dbo.Users - %', SQLERRM;
        WHEN OTHERS THEN
            RAISE NOTICE 'gstusr access to public.Users failed with: %', SQLERRM;
    END;
END
$$;

-- Reset role
RESET ROLE;


-- Set role to usr
SET ROLE usr;

DO $$
BEGIN
    RAISE NOTICE 'Testing access as usr';

    -- Test access to various objects
    PERFORM * FROM gstusr."vwInfoDb";
    RAISE NOTICE 'usr can access gstusr.vwInfoDb';

    PERFORM * FROM supusr."MusicGroups";
    RAISE NOTICE 'usr can access supusr.MusicGroups';

    -- Should fail
    BEGIN
        PERFORM * FROM dbo."Users" LIMIT 5;
        RAISE NOTICE 'ERROR: usr should not access public.Users';
    EXCEPTION
        WHEN insufficient_privilege THEN
            RAISE NOTICE 'EXPECTED: usr cannot access public.Users - %', SQLERRM;
        WHEN OTHERS THEN
            RAISE NOTICE 'usr access to public.Users failed with: %', SQLERRM;
    END;
END
$$;

-- Reset role
RESET ROLE;

-- Set role to supusr
SET ROLE supusr;

DO $$
BEGIN
    RAISE NOTICE 'Testing access as supusr';

    -- Test access to various objects
    PERFORM * FROM gstusr."vwInfoDb";
    RAISE NOTICE 'supusr can access gstusr.vwInfoDb';

    PERFORM * FROM supusr."MusicGroups" LIMIT 5;
    RAISE NOTICE 'supusr can access supusr.MusicGroups';

    -- Should fail
    BEGIN
        PERFORM * FROM dbo."Users" LIMIT 5;
        RAISE NOTICE 'ERROR: supusr should not access public.Users';
    EXCEPTION
        WHEN insufficient_privilege THEN
            RAISE NOTICE 'EXPECTED: supusr cannot access public.Users - %', SQLERRM;
        WHEN OTHERS THEN
            RAISE NOTICE 'supusr access to public.Users failed with: %', SQLERRM;
    END;
END
$$;

-- Reset role
RESET ROLE;

-- Set role to dbo
SET ROLE dbo;

DO $$
BEGIN
    RAISE NOTICE 'Testing access as dbo';

    -- Test access to various objects
    PERFORM * FROM gstusr."vwInfoDb";
    RAISE NOTICE 'dbo can access gstusr.vwInfoDb';

    PERFORM * FROM supusr."MusicGroups" LIMIT 5;
    RAISE NOTICE 'dbo can access supusr.MusicGroups';

    PERFORM * FROM dbo."Users" LIMIT 5;
    RAISE NOTICE 'dbo can access dbo.Users';
END
$$;

-- Reset role
RESET ROLE;