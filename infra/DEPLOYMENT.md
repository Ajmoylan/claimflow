# CLAIMFLOW: Azure deployment runbook

Status: **deployed on 18 Nov 2026** (app in Belgium Central, database in France Central). Live at
https://claimflow-dqst3jxt3iyqy.azurewebsites.net. See [Deployment record](#deployment-record) for what was
verified. Any change that creates or upgrades a resource still waits for explicit approval.

## What gets created

| # | Resource | Name | SKU / configuration | Region | List price | Expected cost |
|---|---|---|---|---|---|---|
| 1 | Resource group | `rg-claimflow` | Tags `project=claimflow`, `environment=demo` | Belgium Central | free | **0 NOK** |
| 2 | App Service plan | `claimflow-plan` | **F1 (Free)**, Linux | Belgium Central | 0.00 NOK/hour | **0 NOK** |
| 3 | Web App | `claimflow-<unique>` | .NET 10 (`DOTNETCORE\|10.0`), HTTPS only, TLS 1.2+, FTP/SCM basic auth off, system-assigned managed identity | Belgium Central | included in plan | **0 NOK** |
| 4 | Azure SQL logical server | `claimflow-sql-<unique>` | Entra ID-only authentication, TLS 1.2+, firewall admits only the Web App's outbound IPv4 addresses (no "Allow Azure services") | France Central | free | **0 NOK** |
| 5 | Azure SQL database | `claimflow` | General Purpose serverless Gen5, 0.5–2 vCores, 32 GB max, LRS backups, **free offer with `AutoPause` on exhaustion** | France Central | after the allowance: 5.32 NOK per vCore-hour, 1.27 NOK/GB-month storage, 1.10 NOK/GB-month backup | **0 NOK** (see below) |
| 6 | Entra app registration and service principal | `claimflow-github-deploy` | Federated credential for `repo:Ajmoylan/claimflow:environment:production`, role **Website Contributor** on the Web App only | (tenant) | free | **0 NOK** |
| 7 | GitHub environment | `production` | Deploys from `main` only; **you approve each deployment** (required reviewer) | (GitHub) | free | **0 NOK** |

`<unique>` is a deterministic hash of the resource group ID (Bicep `uniqueString`). It is known after step 2 of the procedure.

Prices are Microsoft retail list prices in NOK, from the Azure Retail Prices API on 18 Nov 2026.
Outbound data transfer to the internet: the first 100 GB per month is free (0.81 NOK/GB after that).

### Why the app and the database are in different regions

This subscription may only deploy to Belgium Central, Austria East, France Central, Norway East and
Switzerland North. No single one of them has both pieces of the free setup:

| Region | App Service F1 quota | Azure SQL free offer |
|---|---|---|
| France Central | 0 (what-if: `SubscriptionIsOverQuotaForSku`) | offered |
| Belgium Central | available | not offered (`ProvisioningDisabled` when tried) |
| Austria East | available | not offered |
| Norway East, Switzerland North | available / 0 | SQL provisioning restricted for this subscription |

So the app runs in **Belgium Central** and the database in **France Central** (`sqlLocation`). The only cost
this adds is inter-region data transfer between the app and the database: **0.19 NOK/GB**. A demo request
moves a few kilobytes, so even a busy month is far below 1 GB, i.e. **well under 1 NOK per month**, paid
from the student credit. Each database round trip also gets a few milliseconds longer.

### How the free tier behaves, and the one real limitation

- **App Service F1:** free, with a quota of 60 CPU minutes per day and 1 GB memory. There is no Always On, so
  the app unloads when idle and the first request after that is slow (a cold start).
- **Azure SQL free offer:** per database, every month: 100 000 vCore-seconds, 32 GB of data and 32 GB of backup
  storage. Azure *reports* an auto-pause delay of 60 minutes and refuses to change it on a free database
  with `AutoPause` (`az sql db update --auto-pause-delay 15`: "Only default value for auto pause delay is
  allowed"). **Measured on 18 Nov 2026, the database paused about 16 minutes after the app released its
  connections**, three times in a row.
  - Billing per active second is at least the 0.5 vCore minimum, so the allowance is about **55 active
    hours per month**. A visit on its own keeps the database awake for the visit, plus 2 minutes before the
    app releases its connections, plus about 16 minutes: roughly 20 minutes, or 600 vCore-seconds. That is
    about **150 well-spread visits a month** (visits close together share the awake time).
  - The app releases its pooled SQL connections after 2 idle minutes, so the database can start its pause
    pause countdown without waiting for ADO.NET's 4–8 minute pool cleanup.
  - With `freeLimitExhaustionBehavior: AutoPause` (the proposed setting), **nothing is billed**. If the
    allowance runs out, the database stays paused until the 1st of the next month, and the demo shows a
    "could not complete the request" error until then.
  - The alternative, `BillOverUsage`, keeps the demo online and bills about **2.70 NOK per active hour** at
    0.5 vCore. Microsoft does not allow switching back to `AutoPause` after choosing this.
- **Spending is capped regardless:** this subscription is Azure for Students with `spendingLimit: On` and no
  card attached. Charges can only come out of the remaining student credit, and the subscription is
  disabled, not billed, when the credit runs out.

### Unknowns until creation (cannot be checked without creating a resource)

1. **Free-offer eligibility.** Microsoft's documentation excludes only "Azure for Students *Starter*". This
   subscription is the full Azure for Students offer (`AzureForStudents_2018-01-01`), which the
   documentation does not mention either way. If the free offer is refused, the deployment fails without
   creating the database. I would then come back to you with the paid options below instead of picking one.
2. **Entra admin sign-in with your account.** Your sign-in is a personal Microsoft account (an `#EXT#` user
   in its own directory). The documentation lists guest/external users as supported Azure SQL admins but
   does not name personal Microsoft accounts. If sign-in fails in step 4, the documented fallback is to make
   an Entra **security group** `claimflow-sql-admins` (containing you) the admin instead. That is free.
3. **Region.** See "Why the app and the database are in different regions" above.

### Paid fallbacks (only with separate approval)

| Option | Cost per month (list) | When it would be useful |
|---|---|---|
| Azure SQL **Basic** (5 DTU, 2 GB) instead of the free offer | about 45 NOK (1.49 NOK/day) | Free offer refused, or the demo needs to stay online without monthly limits; no pausing, so no database cold start |
| Free offer with `BillOverUsage` | about 2.70 NOK per active hour beyond about 55 hours | Keep the demo online when the allowance runs out |
| App Service **B1** instead of F1 | about 122 NOK (0.167 NOK/hour) | Always On (no app cold start), no daily CPU quota |

## Procedure (each step after approval)

All commands run from the repository root, signed in with `az login` to the Azure for Students subscription.

### 1. Preview: no changes

```bash
az group create --name rg-claimflow --location belgiumcentral --tags project=claimflow environment=demo
export CLAIMFLOW_SQL_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
export CLAIMFLOW_SQL_ADMIN_LOGIN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
az deployment group what-if --resource-group rg-claimflow --template-file infra/main.bicep --parameters infra/main.bicepparam
```

The resource group itself is free. `what-if` lists exactly what would be created, without creating anything.

### 2. Create the infrastructure

```bash
az deployment group create --resource-group rg-claimflow --name claimflow-infra \
  --template-file infra/main.bicep --parameters infra/main.bicepparam
az deployment group show -g rg-claimflow -n claimflow-infra --query properties.outputs
```

### 3. Check the cost settings in Azure (not just in the template)

```bash
az sql db show -g rg-claimflow -s <sqlServerName> -n claimflow \
  --query "{sku:currentSku.name, useFreeLimit:useFreeLimit, onExhaustion:freeLimitExhaustionBehavior}"
az appservice plan show -g rg-claimflow -n claimflow-plan --query sku.name
```

Expected: `useFreeLimit: true`, `onExhaustion: AutoPause`, plan `F1`.

### 4. Create the schema and give the app's managed identity data access (one time)

```bash
brew install sqlcmd   # Microsoft's go-sqlcmd
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create -g rg-claimflow -s <sqlServerName> -n setup-temporary --start-ip-address $MY_IP --end-ip-address $MY_IP
# Schema: applied by you, the SQL admin (see "Apply database migrations" below)
dotnet tool run dotnet-ef migrations script --idempotent --project src/ClaimFlow.Api \
  --context SqlServerClaimFlowDbContext --output /tmp/migrate.sql
sqlcmd -S <sqlServerFqdn> -d claimflow --authentication-method ActiveDirectoryAzCli -b -i /tmp/migrate.sql
# Data access for the app
sed "s/<app-name>/<appName>/g" infra/grant-app-db-access.sql > /tmp/grant.sql
sqlcmd -S <sqlServerFqdn> -d claimflow --authentication-method ActiveDirectoryAzCli -b -i /tmp/grant.sql
az sql server firewall-rule delete -g rg-claimflow -s <sqlServerName> -n setup-temporary
```

The app's identity gets **`db_datareader` and `db_datawriter` only**. It cannot create, alter or drop
tables; nothing it does at runtime needs that (the nightly reset only deletes and inserts rows, and an
integration test on SQL Server runs the whole demo as a login with exactly these two roles). The temporary
firewall rule for your IP address is removed right afterwards.

### Apply database migrations (whenever a change adds one)

The app does not migrate the schema itself in Azure (`Database__MigrateOnStartup=false`). At startup it
checks `__EFMigrationsHistory` and **refuses to start if a migration is pending**, so the deploy job's smoke
test fails visibly instead of the app running against an old schema. Before approving a deployment that
adds a migration, run the same commands as in step 4 without the grant script:

```bash
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create -g rg-claimflow -s <sqlServerName> -n setup-temporary --start-ip-address $MY_IP --end-ip-address $MY_IP
dotnet tool run dotnet-ef migrations script --idempotent --project src/ClaimFlow.Api \
  --context SqlServerClaimFlowDbContext --output /tmp/migrate.sql
sqlcmd -S <sqlServerFqdn> -d claimflow --authentication-method ActiveDirectoryAzCli -b -i /tmp/migrate.sql
az sql server firewall-rule delete -g rg-claimflow -s <sqlServerName> -n setup-temporary
```

The script is idempotent (each migration is guarded by its ID), so running it twice is harmless. Migrations
must stay backward compatible with the running version (add columns and tables first, remove them in a
later release), because the old app keeps serving until the new one is deployed.

Why not migrate from the pipeline? The GitHub deploy identity would then need database rights, and the
SQL firewall would have to admit GitHub's changing runner addresses. Applying the reviewed script as the
SQL admin keeps both the deploy identity and the runtime identity narrow, and schema changes are rare here.

**Network access.** The F1 tier cannot join a virtual network or use a private endpoint, so the app reaches
the database's public endpoint. Instead of the broad "Allow Azure services" rule (which admits any Azure
customer's traffic), the template adds one firewall rule per address in the Web App's
`possibleOutboundIpAddresses` (`modules/sqlFirewall.bicep`). On the shared F1 tier these addresses belong to
the App Service scale unit, so other apps on it share them; a connection still needs an Entra ID identity
that has a database user, and only the app's managed identity (and the admin) has one. If App Service ever
moves the app to new addresses, re-running the template updates the rules.

### 5. Let GitHub Actions deploy with OIDC (no secrets)

```bash
APP_ID=$(az ad app create --display-name claimflow-github-deploy --query appId -o tsv)
SP_ID=$(az ad sp create --id $APP_ID --query id -o tsv)
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:Ajmoylan@197133630/claimflow@1462022359:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
WEBAPP_ID=$(az webapp show -g rg-claimflow -n <appName> --query id -o tsv)
az role assignment create --assignee-object-id $SP_ID --assignee-principal-type ServicePrincipal \
  --role "Website Contributor" --scope $WEBAPP_ID
```

The deploy identity can only deploy to this one Web App. It cannot read the database or change any
other resource.

**The subject must match GitHub's token exactly.** This repository uses GitHub's *immutable* OIDC subject
format (`gh api repos/Ajmoylan/claimflow/actions/oidc/customization/sub` shows `use_immutable_subject: true`),
which embeds the owner and repository IDs: `repo:<owner>@<owner-id>/<repo>@<repo-id>:environment:production`.
The older name-only form (`repo:Ajmoylan/claimflow:environment:production`) fails with `AADSTS700213: No
matching federated identity record found`. The ID form is also stricter: a repository later recreated under
the same name gets a new ID and cannot use this credential.

### 6. Configure GitHub

- Create the `production` environment: deployments from `main` only, with you as required reviewer, so
  every deployment waits for your click in GitHub.
- Set repository variables (IDs, not secrets): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`.

### 7. First deployment

Re-run CI on `main` (or merge the next PR). The `deploy` job waits for your approval, deploys the artifact
built and tested in that same run, and smoke-tests `/health` and `/api/dashboard/summary`.

### 8. Verify

- The live URL loads, with the "fictional demo" banner and seed data; `/docs` and `/health` work.
- Security headers include HSTS. No connection string or secret appears in App Service settings beyond the
  managed-identity connection string.
- Logs (`az webapp log tail`) are JSON, with no personal data.
- After the first visit following 03:00 Oslo time, the data has been reset.

## Deployment record

Deployed 18 Nov 2026 from `main` by the CI `deploy` job (approved in the `production` environment).

**Resources, as Azure reports them**

| Resource | Region | Configuration |
|---|---|---|
| App Service plan `claimflow-plan` | Belgium Central | F1 (Free), Linux |
| Web App `claimflow-dqst3jxt3iyqy` | Belgium Central | .NET 10, HTTPS only, TLS 1.2, FTP and SCM basic auth off, system-assigned managed identity |
| SQL server `claimflow-sql-dqst3jxt3iyqy` | France Central | Entra ID-only, TLS 1.2, 31 firewall rules (one per Web App outbound IPv4), no `0.0.0.0` rule |
| Database `claimflow` | France Central | GP_S_Gen5, 0.5–2 vCores, 32 GB, LRS backup, `useFreeLimit: true`, `freeLimitExhaustionBehavior: AutoPause` |
| App registration `claimflow-github-deploy` | (tenant) | No client secret; one federated credential (immutable subject, `production` environment); Website Contributor on the Web App only |

Container stdout logging to the App Service file system is on (3 days, 100 MB cap; free).

**Cost:** Cost Management shows 0 NOK month to date for `rg-claimflow`. The only expected charge is
inter-region transfer between the app and the database (0.19 NOK/GB), well under 1 NOK a month.

**Live tests (18 Nov 2026)**

- API, 27/27 checks: reads, search and filters (literal `%`), create (201 + `Location`), the full workflow
  including 409 for skipped or final states and 400 for a rejection without a reason, audit history,
  validation, expired and unknown policies, malformed JSON, 413 over 16 KB.
- Browser, desktop and 375 px mobile: dashboard, list and filters, create through the form, review and
  reject with a reason in the UI, audit history; no horizontal page scroll; form inputs at 16 px.
- Security: CSP, HSTS, nosniff, `X-Frame-Options: DENY`; HTTP redirects to HTTPS; TLS 1.0 and 1.1 refused;
  no config or DLL files served; Kudu basic auth refused (401); no CORS; the SQL server refuses this
  developer's IP (error 40615); 120 API requests/min then 429; writes 429 with `Retry-After: 60`.

**Database cold starts** (database paused, first visit afterwards):

| Run | What the visitor saw | Server side |
|---|---|---|
| 1 (dashboard, browser) | App shell in 1.5 s with "Loading dashboard… (the demo database may take a few seconds to wake up)"; data after about 68 s | First API call returned 500 after 8.3 s; the frontend's automatic retry succeeded after 56 s. Container logging was not yet on, so the exception was not captured. |
| 2 (`curl`, dashboard API) | 200 after 52.9 s | First connection attempt failed after about 42 s; EF Core retried and succeeded |
| 3 (claims list, browser) | Filters at once, "Loading…", then the list after 56.8 s | Same as run 2: one failed attempt, then success |

A resume takes about 55 seconds. The page never breaks: it shows its loading message while the
server-side retries (EF Core, up to 6) and the frontend's retries (up to 2 for 5xx and network errors) run.
EF Core retries are logged as warnings, so if the run-1 500 happens again its SQL error is in the log.

## Teardown (removes everything, stops all cost)

```bash
az group delete --name rg-claimflow --yes
az ad app delete --id <APP_ID>
```

Then delete the GitHub `production` environment and the four `AZURE_*` variables.
