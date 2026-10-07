# BulkEmailSender

Angular frontend and ASP.NET Core .NET 8 API for single-user bulk email sending through Gmail. The backend serves the Angular production files, API and Server-Sent Events (SSE) endpoint, and runs the background send worker in the same process.

## Deploy to Render Free

Render builds the root [Dockerfile](Dockerfile) as a Docker Web Service. No Nginx, Docker Compose, VPS, persistent disk, external database or object storage is used by this deployment. Render terminates HTTPS and routes requests to the ASP.NET process.

### Create the Web Service

1. Push this repository to GitHub.
2. In Render, choose **New → Web Service**, connect GitHub, and select the repository and deploy branch.
3. Select **Docker** as the runtime, choose the **Free** instance type, and leave the Dockerfile path as `./Dockerfile` with the repository root as the build context.
4. Set the environment variables below in the Render Dashboard before deploying. Set `ASPNETCORE_ENVIRONMENT=Production`; configure `/health` as the service health check path.
5. Create the service and wait for the Docker build and initial deploy to complete.
6. Open the generated `https://<service-name>.onrender.com` URL. Check `/health`, load the Angular app, sign in, connect Gmail, and perform a test send to an address you control. Check that row updates arrive over the send progress stream.

Render supplies `PORT` to the container. The Docker entrypoint binds ASP.NET Core to `0.0.0.0:$PORT` (falling back to 10000 for local container runs); do not set a fixed external production port. The image uses Node 22 for Angular and .NET 8 SDK/runtime stages, and its final stage contains the published API, static frontend and health-check utility only.

### Render environment variables

Add these under the Web Service's **Environment** settings. Use **Secret** values for the Google client secret and application password hash.

| Name | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | `Data Source=/app/data/bulk-email-sender.db;Cache=Shared;Default Timeout=30` |
| `DataProtection__KeysPath` | `/app/data/keys` |
| `GoogleOAuth__ClientId` | Google OAuth web client ID |
| `GoogleOAuth__ClientSecret` | Google OAuth web client secret (secret) |
| `GoogleOAuth__RedirectUri` | `https://<actual-service-name>.onrender.com/api/auth/google/callback` |
| `GoogleOAuth__AngularReturnUri` | `https://<actual-service-name>.onrender.com/` |
| `AppAuth__PasswordHash` | PBKDF2 hash for the single-user application login (secret) |

Render sets `PORT`; do not manually add an `ASPNETCORE_URLS` value. The callback and return URLs must match the actual Render service hostname. If you later add a custom domain, update both OAuth URLs and the Google Cloud redirect URI to use it.

Generate `AppAuth__PasswordHash` on a trusted machine. This reads the password without echo and emits the format the API expects:

```sh
python3 - <<'PY'
import base64, getpass, hashlib, secrets
password = getpass.getpass('Application password: ').encode()
salt = secrets.token_bytes(16)
iterations = 600000
derived = hashlib.pbkdf2_hmac('sha256', password, salt, iterations)
print(f"pbkdf2-sha256${iterations}${base64.b64encode(salt).decode()}${base64.b64encode(derived).decode()}")
PY
```

Paste the output as the secret environment value. Do not put the plaintext password, hash, Google secret or any production secret in Git, appsettings, Angular, or the Dockerfile. `deployment/.env.example` is only for the optional VPS Compose setup, not Render.

### Google Cloud OAuth

In Google Cloud, configure an External OAuth consent app as appropriate, enable Gmail API, and configure the app's scopes (`openid`, `email`, `profile`, `https://www.googleapis.com/auth/gmail.send`). Add this exact authorized redirect URI, using the service URL shown in Render:

```text
https://<actual-service-name>.onrender.com/api/auth/google/callback
```

If the consent app remains in Testing, add the intended Google account as a test user. Consider Google's verification and publishing requirements for production. Keep `http://localhost:5016/api/auth/google/callback` only for local development; do not replace the production URI with it. Rotate the previous OAuth client secret that was committed in an earlier appsettings version; it has been removed from the current config but may remain in Git history.

### SQLite and Free service behavior

The app uses `/app/data/bulk-email-sender.db` and applies EF Core migrations on every Production startup. A new or empty filesystem therefore creates a fresh schema; migration errors are logged at critical level and stop startup. SQLite, Gmail OAuth rows, Data Protection keys and application log files are local ephemeral files. This deployment intentionally does not add a persistent disk or external storage. Gmail sending uses the existing HTTPS Gmail API integration, not SMTP. The Docker build context excludes ignored local development settings, databases, secrets and dependency/build outputs through `.dockerignore`.

Data may disappear on service restart, redeploy, instance replacement or idle spin-down. After that, the app starts with an empty database, Gmail is disconnected and the app login cookie may need to be reissued because its Data Protection keys were also ephemeral. Sign into the app and reconnect Gmail. Render Free services spin down after 15 minutes without inbound traffic and the first request can take about a minute while the service wakes. No keep-alive traffic is configured. Free web services also have monthly instance-hour, bandwidth and build-minute limits; see [Render's Free plan details](https://render.com/docs/free).

### Routes, SSE and health

ASP.NET serves Angular from the same origin. `/` and client routes such as `/send` load `index.html`; `/api/*` remains API-only, with unmatched API routes returning 404. Angular calls relative `/api/...` URLs. Render terminates HTTPS and forwards HTTP to the container; forwarded headers are enabled so secure cookies and HTTPS redirection use the original scheme. No CORS policy is needed.

The existing SSE endpoint remains `/api/send/{operationId}/events`, returns `text/event-stream`, flushes events from ASP.NET Core, and uses the in-process send worker. There is no Nginx SSE proxy in Render deployment. `/health` checks app/database availability and does not require Gmail to be connected or reveal secrets.

### Custom domain (optional)

The generated `onrender.com` URL is enough to deploy. To add `mail.example.com`, add the domain under the service's Render Dashboard settings, then configure the DNS record Render specifies and verify it in the Dashboard. Render provisions and renews TLS and redirects HTTP to HTTPS. Update the Render OAuth callback/return environment variables and Google Cloud authorized redirect URI afterward. See [Render custom domains](https://render.com/docs/custom-domains).

### Updates and logs

With GitHub connected, pushes to the selected branch trigger Render builds and deploys. Use the Render Deploys and Logs pages to review build output, application startup/migration messages, OAuth status, worker activity and errors. A new deploy may reset all SQLite and Gmail connection data by design. Render's Free plan rollback history is limited; filesystem state is not rolled back with an image.

## Local development

The Render changes preserve the existing workflow:

```sh
cd BulkEmailSenderFrontend
npm install
npm start
```

Run the API using the existing `dotnet run` workflow from its project directory. Angular's existing `proxy.conf.json` continues forwarding local `/api` requests to the API. Local Google OAuth keeps its development callback at `http://localhost:5016/api/auth/google/callback`; configure local credentials in the ignored development settings. Production OAuth values belong in Render environment settings.

## Optional VPS deployment files

The old VPS Compose/Nginx setup remains under `deployment/` and is documented separately in [VPS-DEPLOYMENT.md](BulkEmailSender/VPS-DEPLOYMENT.md). Render does not use it.
