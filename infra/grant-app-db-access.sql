-- Run once after the first deployment, connected to the claimflow database as the Entra SQL administrator.
-- Replace <app-name> with the Web App name from the deployment output.
-- Gives the app's managed identity exactly what it needs: read/write data, and create/alter tables
-- because the app applies EF Core migrations at startup.
CREATE USER [<app-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<app-name>];
ALTER ROLE db_datawriter ADD MEMBER [<app-name>];
ALTER ROLE db_ddladmin ADD MEMBER [<app-name>];
