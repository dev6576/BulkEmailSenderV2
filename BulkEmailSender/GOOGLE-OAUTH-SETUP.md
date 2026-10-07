# Google OAuth and Gmail setup

This guide configures the existing Angular + ASP.NET Core application for local Gmail sending. OAuth is a **server-side web application flow**: Angular starts the connection, while the backend redirects to Google, receives the callback, exchanges the one-time authorization code, stores tokens, and calls Gmail. Each person authorizes their own Google account; the OAuth client credentials identify the application.

## 1. Prerequisites

- .NET 8 SDK
- Node.js and npm
- A Google account for the Cloud Console
- A Google Cloud project where you can enable APIs and create OAuth credentials
- EF Core command-line tool 8.0.20 (`dotnet tool install --global dotnet-ef --version 8.0.20`)

Run the commands below from the repository root unless the step says otherwise.

## 2. Configure Google Cloud

1. Open the [Google Cloud Console](https://console.cloud.google.com/) and select or create a project for this application.
2. In **APIs & Services → Library**, find and enable **Gmail API**.
3. Open **Google Auth Platform** and configure the consent screen:
   - Set the app name, support email, and developer contact email.
   - For a personal Gmail account, choose **External** for the audience.
   - While the app is in testing, add every account that will connect it under **Test users**.
4. Under **Data Access**, add only these scopes:

   ```text
   openid
   email
   profile
   https://www.googleapis.com/auth/gmail.send
   ```

   `gmail.send` allows the app to send mail on a user's behalf. The app does not need Gmail inbox or message-reading scopes. Google classifies `gmail.send` as sensitive and may require OAuth verification for public use. See [Gmail API scope classifications](https://developers.google.com/workspace/gmail/api/auth/scopes).

5. Under **Clients**, create an OAuth client with application type **Web application**.
6. Give the client a recognizable name, such as `BulkEmailSender local development`.
7. Add this exact value under **Authorized redirect URIs**:

   ```text
   http://localhost:5016/api/auth/google/callback
   ```

   The scheme, host, port, path, and trailing slash (if any) must match exactly. See [Google's web-server OAuth guide](https://developers.google.com/identity/protocols/oauth2/web-server).

8. Leave **Authorized JavaScript origins** empty for this app's current flow. Angular navigates to the backend, and the backend handles redirects and token exchange; Angular does not call Google's OAuth endpoints directly.
9. Create the client and copy its **Client ID** and **Client secret** for the next step.

## 3. Configure backend credentials

Do not put the client secret in Angular, source code, or a committed `appsettings` file. For local development, store it with .NET User Secrets.

Initialize User Secrets once for the API project:

```powershell
dotnet user-secrets init --project .\BulkEmailSender\src\BulkEmailSender.Api
```

Set the client ID and endpoints:

```powershell
$clientId = Read-Host "Google OAuth Client ID"
dotnet user-secrets set "GoogleOAuth:ClientId" $clientId --project .\BulkEmailSender\src\BulkEmailSender.Api
dotnet user-secrets set "GoogleOAuth:RedirectUri" "http://localhost:5016/api/auth/google/callback" --project .\BulkEmailSender\src\BulkEmailSender.Api
dotnet user-secrets set "GoogleOAuth:AngularReturnUri" "http://localhost:4200/" --project .\BulkEmailSender\src\BulkEmailSender.Api
```

Set the client secret without entering it as a literal command argument:

```powershell
$clientSecret = Read-Host "Google OAuth Client Secret" -AsSecureString
$plainSecret = [System.Net.NetworkCredential]::new("", $clientSecret).Password
dotnet user-secrets set "GoogleOAuth:ClientSecret" $plainSecret --project .\BulkEmailSender\src\BulkEmailSender.Api
Remove-Variable plainSecret, clientSecret
```

`GoogleOAuth:AngularReturnUri` already defaults to `http://localhost:4200/` in `appsettings.json`; set it explicitly as above so the local values are clear. For another port or host, update both this value and the registered Google redirect URI as appropriate.

For a hosted backend, configure these environment variables or use a secret manager instead:

```text
GoogleOAuth__ClientId
GoogleOAuth__ClientSecret
GoogleOAuth__RedirectUri
GoogleOAuth__AngularReturnUri
```

The client ID and secret are configured once per backend deployment, not once per Gmail user. Each user independently grants access, producing user-specific tokens stored by the backend.

## 4. Apply the database migration

The migration adds Gmail connection and OAuth state tables and adds an owner field to send operations. It preserves existing records; older operations receive an empty owner and won't be visible through the new session-owned SSE endpoint.

Run:

```powershell
dotnet ef database update --project .\BulkEmailSender\src\BulkEmailSender.Api --startup-project .\BulkEmailSender\src\BulkEmailSender.Api
```

Do not delete or recreate the SQLite database to apply this migration.

## 5. Start the application

After completing the Google Cloud and User Secrets setup above, you can run the root-level `run-local.bat` from File Explorer or a terminal. It checks the required tools and OAuth settings, installs npm dependencies if they are missing, applies the database migration, starts the backend and frontend in separate command windows, and opens the browser. Keep both command windows open. If the script stops with an error, follow the message and retry.

To start the services manually instead, use two PowerShell terminals:

Terminal 1 — backend:

```powershell
dotnet run --project .\BulkEmailSender\src\BulkEmailSender.Api --launch-profile http
```

The backend listens at `http://localhost:5016` for the `http` launch profile.

Terminal 2 — Angular frontend:

```powershell
Set-Location .\BulkEmailSenderFrontend
npm start
```

The Angular dev server listens at `http://localhost:4200` and proxies `/api` requests to the backend.

## 6. Connect and verify Gmail

1. Open [http://localhost:4200](http://localhost:4200).
2. The header should show **Gmail not connected**.
3. Click **Connect Gmail**.
4. Choose a Google account that is listed as a consent-screen test user.
5. Review and approve the requested Gmail sending permission.
6. Google returns to the backend callback, then the backend redirects to Angular.
7. The header should show **Connected to Gmail** and the connected email address.
8. The status endpoint can also be viewed at [http://localhost:4200/api/auth/status](http://localhost:4200/api/auth/status). It returns connection state and email only, never OAuth tokens.
9. Send a small test message to an address you control. Gmail sending limits still apply.
10. Use **Disconnect** in the header to deactivate the connection.

## 7. Common setup errors

- **`redirect_uri_mismatch`:** Check that the Cloud Console redirect URI and `GoogleOAuth:RedirectUri` are exactly `http://localhost:5016/api/auth/google/callback`.
- **Google says the app is unavailable or access is blocked:** Confirm the signing-in account is on the consent screen's test-user list and that the app audience is correct.
- **The backend reports OAuth is not configured:** Re-run the User Secrets commands for the API project and restart the backend.
- **The header remains disconnected:** Check that the backend is running on port `5016`, Angular is on `4200`, and the browser is using the same host (`localhost` consistently, not a mix of `localhost` and `127.0.0.1`).
- **Google displays an unverified-app warning:** This can occur during testing. Only continue with accounts you trust and have added as test users. Public use of the sensitive Gmail send scope requires Google's applicable verification.

## 8. Sharing or deploying the application

- If your friend runs an independent copy, they should create their own Google Cloud project/OAuth web client and configure their own backend secrets and redirect URI.
- If you operate one shared hosted application, use one OAuth client for that deployment. Keep its secret on the backend; never distribute it to end users or commit it to the repository.
- For production, use HTTPS and register the production callback URI. Persist and protect the ASP.NET Core Data Protection key ring so stored tokens remain decryptable after restarts and across instances.
- This repository does not yet have real application accounts. Its ownership boundary is a protected browser-session cookie, not authenticated user identity. Add application authentication and authorization before offering a shared multi-user deployment; the current session identifier is suitable only for local development and limited evaluation.
