-- Run once after the first deployment, connected to the claimflow database as the Entra SQL administrator.
-- Replace <app-name> with the Web App name from the deployment output.
-- Least privilege: the app's managed identity may read and write data, nothing else. It cannot create,
-- alter or drop tables; schema changes are applied by the SQL admin with the migration script
-- (infra/DEPLOYMENT.md, "Apply database migrations"). The nightly demo reset only uses DELETE and INSERT.
CREATE USER [<app-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<app-name>];
ALTER ROLE db_datawriter ADD MEMBER [<app-name>];

-- Existing deployments created before this change also granted db_ddladmin. Remove it with:
--   ALTER ROLE db_ddladmin DROP MEMBER [<app-name>];
