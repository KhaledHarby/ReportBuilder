# Implement portal → Report Builder launch SSO

**This project is Node.js + React.** Auth uses **your own custom token generator**, not JWT. Do not change that. Do not send those tokens to Report Builder.

Copy this entire file into Cursor in the **other system** (MECS / portal). Implement only in **that** repo. Report Builder already has the receiving side.

---

## Cursor mission (read this first)

Implement a **Node.js server-side launch** so a user who is already logged into this React app can open Report Builder **without a second login**.

This app’s session stays as it is today: custom tokens, cookies, `Authorization` header — whatever you already use. Report Builder does not understand those tokens and must never receive them.

Do **not**:

- Build a second login form
- Send this app’s password to Report Builder
- Send this app’s custom access/refresh/session token to Report Builder
- Put any token (yours or Report Builder’s) in a URL
- Call Report Builder `/api/auth/exchange`, `/api/auth/login`, or `/api/auth/refresh` from this app
- Call Report Builder `/api/auth/launch` from React / the browser
- Replace your token generator with JWT to make this work

The only work in this repo is:

1. Store Report Builder API URL + shared secret in **Node server** env/config (never `REACT_APP_*` / Vite `VITE_*` that ships to the browser).
2. Add an **authenticated Node route** that:
   - uses your existing token middleware to know who is logged in
   - server-to-server `POST`s Report Builder `/api/auth/launch` with the **shared secret** and **user profile fields only**
3. Return `{ redirectUrl }` to React (or 302). React does `window.location.assign(redirectUrl)`.
4. Add a React button/menu item for signed-in users only.
5. Optionally pass a sanitized `returnPath` (which Report Builder page to open).

Set `source` to a stable name for this system. For MECS use `"MECS"`.

---

## Stack assumptions

| Layer | This project | Report Builder (already done) |
|---|---|---|
| API | Node.js (Express / Koa / Fastify / Nest — use whatever you have) | ASP.NET |
| UI | React | React |
| User session | **Custom token generator** (opaque tokens, signed blobs, session ids — not JWT) | JWT **after** `/sso` exchange, only inside Report Builder |
| Bridge | Shared launch secret + one-time `code` | Same |

Two separate sessions. After redirect, the user is signed into Report Builder with **Report Builder’s** cookie/JWT. Your token stays in your app. They are not the same token and are not interchangeable.

---

## Background

**Report Builder** is DMT’s report / analytics app. Users can still sign in there with a local account.

**This portal** already has users and a custom token session. Staff should click **Open Report Builder** and land in Report Builder already signed in.

```
React (user already has your custom token)
    → GET/POST your Node route  /api/report-builder/open
         (your existing auth middleware reads YOUR token)
    → Node server POST {ReportBuilder}/api/auth/launch
         headers: X-Launch-Secret  (shared secret, NOT the user token)
         body:    userName, externalId, names, email, source, returnPath
    → Report Builder returns { code, expiresAt, redirectUrl }
    → Node returns { redirectUrl } to React
    → browser navigates to redirectUrl
    → Report Builder page /sso?code=…
    → Report Builder React calls POST /api/auth/exchange
    → Report Builder issues ITS OWN session (JWT). Your token is not used.
```

Report Builder JIT-creates the user on first launch and assigns **Portal viewer** (`reports.view`, `reports.export`, `analytics.view`, `insights.view`). Later launches update name/email and reuse the same person when `(source, externalId)` match.

Local Report Builder `/login` stays for break-glass admin. Do not try to disable it.

---

## What is already finished in Report Builder (do not reimplement)

| Item | Status |
|---|---|
| `POST /api/auth/launch` | Done. Server-to-server. Shared secret required. |
| `POST /api/auth/exchange` | Done. Called only by the Report Builder SPA. |
| `GET /api/auth/options` | Done. Public. `{ externalLoginUrl, launchEnabled }`. |
| SPA route `/sso?code=&return=` | Done. |
| JIT user + Portal viewer role | Done. |
| Local username/password login | Unchanged. |

Do **not** add Report Builder identity tables, JWT libraries for RB, or exchange logic here.

---

## Security rules (hard)

1. **Shared secret lives only on the Node server.** `process.env.REPORT_BUILDER_LAUNCH_SECRET`. Never put it in React env that is inlined into the bundle.
2. **`/api/auth/launch` is Node → Report Builder only.** React must call **your** API. If launch is called from the browser, the secret is stolen.
3. **Your custom tokens stay in this app.** Do not put them in the launch body, query string, or `Authorization` toward Report Builder. Report Builder’s `Authorization: Bearer` on launch is **only** an alternate way to send the **shared secret**, not a user token.
4. **Never send this app’s password** to Report Builder.
5. **Never put Report Builder access/refresh tokens in the URL.** Only the short-lived `code` goes in the URL, and Report Builder puts it there via `redirectUrl`.
6. **Redirect only to `redirectUrl`.** Do not invent `/sso?code=` yourself unless `redirectUrl` is a relative path you must prefix with the SPA origin.
7. **`returnPath`** must start with a single `/`. No `//`, no `https://`, no `javascript:`.
8. Codes expire in about **60 seconds** and are **one-time**. Do not cache. Do not retry a used code. Call launch again.
9. **Identity comes from your validated token**, not from React query/body `userName`. If the client can send `userName=admin`, that is an account-takeover bug.

---

## Configuration (Node server only)

`.env` (server). Do not prefix with `REACT_APP_` or `VITE_`.

**Local / development**

```
REPORT_BUILDER_API_BASE_URL=http://localhost:5244
REPORT_BUILDER_LAUNCH_SECRET=
REPORT_BUILDER_SOURCE=MECS
REPORT_BUILDER_PUBLIC_URL=http://localhost:5173
```

Fill `REPORT_BUILDER_LAUNCH_SECRET` with the same value as Report Builder `Auth:Launch:Secret` on your machine.

**Production** (Report Builder is at `https://mecs.dmt.gov.ae/ReportBuilder`)

```
REPORT_BUILDER_API_BASE_URL=https://mecs.dmt.gov.ae/ReportBuilder
REPORT_BUILDER_LAUNCH_SECRET=
REPORT_BUILDER_SOURCE=MECS
REPORT_BUILDER_PUBLIC_URL=https://mecs.dmt.gov.ae/ReportBuilder
```

Launch call: `POST https://mecs.dmt.gov.ae/ReportBuilder/api/auth/launch`  
Browser lands on: `https://mecs.dmt.gov.ae/ReportBuilder/sso?code=…`

`REPORT_BUILDER_LAUNCH_SECRET` must match production Report Builder `Auth:Launch:Secret`. Put it in the Node server environment / secret store, not in git.

| Setting | Dev | Production | Notes |
|---|---|---|---|
| `REPORT_BUILDER_API_BASE_URL` | `http://localhost:5244` | `https://mecs.dmt.gov.ae/ReportBuilder` | No trailing slash. Launch path is `{base}/api/auth/launch`. |
| `REPORT_BUILDER_LAUNCH_SECRET` | same as local RB | same as production RB | Never ship in the React bundle. |
| `REPORT_BUILDER_SOURCE` | `MECS` | `MECS` | Sent as `source`. |
| `REPORT_BUILDER_PUBLIC_URL` | `http://localhost:5173` | `https://mecs.dmt.gov.ae/ReportBuilder` | Fallback if `redirectUrl` is relative (`/sso?…`). |

**Ops must also set on Report Builder** (already in `appsettings.Production.json` except the secret):

- `Auth:Launch:PublicBaseUrl` = `https://mecs.dmt.gov.ae/ReportBuilder`
- `Auth:Launch:Secret` = same value as `REPORT_BUILDER_LAUNCH_SECRET`
- Restart / republish Report Builder
- `GET https://mecs.dmt.gov.ae/ReportBuilder/api/auth/options` → `"launchEnabled": true`

Optional on Report Builder: `Auth:Launch:ExternalLoginUrl` = a URL **in this React app** that, after your login, hits your open route. Not required for the button inside this app.

Until Report Builder `Secret` is set, launch returns **503**.

---

## HTTP contract with Report Builder (Node calls this)

### Request

```
POST {REPORT_BUILDER_API_BASE_URL}/api/auth/launch
Content-Type: application/json
X-Launch-Secret: {REPORT_BUILDER_LAUNCH_SECRET}
```

Prefer `X-Launch-Secret`. Do **not** send the user’s custom token as `Authorization`.

JSON body (camelCase):

```json
{
  "userName": "khaled.harby",
  "displayName": "Khaled Harby",
  "displayNameArabic": "خالد حربي",
  "email": "khaled.harby@dmt.gov.ae",
  "externalId": "12345",
  "source": "MECS",
  "returnPath": "/analytics"
}
```

| Field | Required | Map from **your authenticated user** (after your token middleware) |
|---|---|---|
| `userName` | **Yes** | Login name. `DOMAIN\user` or email is OK; Report Builder strips `DOMAIN\` and `@…`. |
| `externalId` | **Strongly recommended** | Stable user id / primary key that does not change when they rename the login. |
| `source` | Recommended | `"MECS"` (or this system’s name). |
| `displayName` | Recommended | English name. |
| `displayNameArabic` | Recommended if you have it | Arabic name. |
| `email` | Optional | Real email only. Omit if unknown. |
| `returnPath` | Optional | Sanitized SPA path inside Report Builder. |

Report Builder matching:

1. `(source, externalId)`
2. Else `(userName, source)` — existing SSO users from this source only
3. Else create user. If `userName` is already a **local** RB account (`admin`), they get `admin-2`

Do not bind by email.

### Success (200)

```json
{
  "code": "<one-time opaque string>",
  "expiresAt": "2026-09-08T19:12:00+00:00",
  "redirectUrl": "https://host/ReportBuilder/sso?code=...&return=/analytics"
}
```

Give React **`redirectUrl` only**. Do not log the full `code` in production.

If `redirectUrl` is relative (`/sso?code=…`), prefix with `REPORT_BUILDER_PUBLIC_URL` (no double slash).

### Errors (ProblemDetails-style JSON)

Read `detail` or `title` or `message`.

| HTTP | Meaning | What this app should do |
|---|---|---|
| 400 | Invalid user name / payload | Show a safe error. Fix field mapping. |
| 401 | Shared secret missing or wrong | Log server-side. Do not show the secret. |
| 403 | That Report Builder account is deactivated | Ask a Report Builder admin to activate them. |
| 503 | Launch not configured on Report Builder | Secret still empty. Contact RB ops. |

---

## What to implement

### 1. Node: launch client (server-only)

New module, e.g. `server/reportBuilder/launchClient.js`.

- `fetch` or `axios` from Node
- 10s timeout
- header `X-Launch-Secret`
- body = profile fields + `source` + optional `returnPath`
- **no** user token on that outgoing request

### 2. Node: authenticated route

```
GET  /api/report-builder/open?returnPath=/analytics
POST /api/report-builder/open
```

Reuse **the same middleware** you use for any private API (the one that validates your custom token and sets `req.user`).

```js
// Express-shaped example — adapt to Koa/Fastify/Nest
router.get(
  '/api/report-builder/open',
  requireAuth, // YOUR existing custom-token middleware
  async (req, res) => {
    const user = req.user // from YOUR token, not from query
    const returnPath = sanitizeReturnPath(req.query.returnPath)

    const ticket = await launchReportBuilder({
      userName: user.userName || user.username || user.login,
      externalId: String(user.id ?? user.userId ?? user.uid),
      displayName: user.displayNameEn || user.fullName || user.name,
      displayNameArabic: user.displayNameAr || user.nameAr || null,
      email: user.email || null,
      returnPath,
    })

    res.json({ redirectUrl: ticket.redirectUrl })
    // or: res.redirect(302, ticket.redirectUrl)
  },
)
```

`sanitizeReturnPath`:

- empty → `null`
- must start with `/`
- reject `//` and anything containing `://`
- max 256 chars

If `requireAuth` fails, return **your** usual 401. The React app should send the user to **your** login, not Report Builder login.

### 3. React: button

Signed-in users only. Call **your** Node route with the user’s **existing** `Authorization` / cookie (same as every other private call).

```jsx
async function openReportBuilder(returnPath) {
  const response = await api.get('/api/report-builder/open', {
    params: returnPath ? { returnPath } : undefined,
  })
  const url = response.data.redirectUrl
  if (!url) throw new Error('Report Builder did not return a redirect')
  window.location.assign(url)
}
```

- EN: `Open Report Builder`
- AR: `فتح منشئ التقارير`

Put it in the header/sidebar. Optional extra links on a report/task with a `returnPath`.

Do **not** `fetch('http://localhost:5244/api/auth/launch')` from React.

### 4. Deep links (`returnPath`)

These are Report Builder SPA paths **after** its public base (`/ReportBuilder` in prod is added by RB, not by you):

| Destination | `returnPath` |
|---|---|
| Report list | `/` or omit |
| One report | `/reports/{guid}/view` |
| Embedded report | `/embed/reports/{guid}` |
| Analytics library | `/analytics` |
| One dashboard | `/analytics/{guid}/view` |
| Insights | `/insights` |

Do not pass `/sso` or `/login`.

### Embed a report (iframe, no second login)

Use `returnPath` `/embed/reports/{guid}`. Optional query `lang=ar` or `lang=en`.

Report Builder returns `redirectUrl` pointing at that frame with a one-time `code` (about 60 seconds, single use). Put **only** `redirectUrl` in an iframe. Do not send the user to `/login`, and do not build the URL yourself.

```html
<iframe
  title="Report"
  src="{redirectUrl}"
  style="width:100%;height:720px;border:0"
></iframe>
```

The frame shows the report filters and the grid or chart. It does not show the studio sidebar or the report title bar. Request a new launch for each time the frame is opened. A used or expired code shows a short message in the frame, not a login form.

### 5. Optional return entry

If Report Builder `ExternalLoginUrl` points here, use a route that requires your login and then runs the same open action.

---

## Suggested Node implementation sketch

Adapt to this repo’s router, env loader, and user object. This is the intended shape.

```js
// server/reportBuilder/launchClient.js
const BASE = process.env.REPORT_BUILDER_API_BASE_URL
const SECRET = process.env.REPORT_BUILDER_LAUNCH_SECRET
const SOURCE = process.env.REPORT_BUILDER_SOURCE || 'MECS'
const PUBLIC_URL = (process.env.REPORT_BUILDER_PUBLIC_URL || '').replace(/\/$/, '')

function sanitizeReturnPath(value) {
  const path = String(value || '').trim()
  if (!path) return null
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('://')) return null
  return path.length > 256 ? path.slice(0, 256) : path
}

function resolveRedirect(redirectUrl) {
  if (!redirectUrl) throw new Error('Empty redirectUrl')
  if (/^https?:\/\//i.test(redirectUrl)) return redirectUrl
  if (redirectUrl.startsWith('/') && PUBLIC_URL) return `${PUBLIC_URL}${redirectUrl}`
  return redirectUrl
}

async function launchReportBuilder({
  userName,
  externalId,
  displayName,
  displayNameArabic,
  email,
  returnPath,
}) {
  if (!SECRET) {
    const err = new Error('Report Builder launch secret is not configured')
    err.status = 503
    throw err
  }

  const response = await fetch(`${BASE.replace(/\/$/, '')}/api/auth/launch`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Launch-Secret': SECRET,
    },
    body: JSON.stringify({
      userName,
      displayName: displayName || undefined,
      displayNameArabic: displayNameArabic || undefined,
      email: email || undefined,
      externalId: externalId || undefined,
      source: SOURCE,
      returnPath: sanitizeReturnPath(returnPath),
    }),
    signal: AbortSignal.timeout(10_000),
  })

  const payload = await response.json().catch(() => ({}))
  if (!response.ok) {
    const err = new Error(payload.detail || payload.title || payload.message || 'Launch failed')
    err.status = response.status
    throw err
  }

  return { redirectUrl: resolveRedirect(payload.redirectUrl) }
}

module.exports = { launchReportBuilder, sanitizeReturnPath }
```

React (use your existing `api` helper that already attaches the custom token):

```jsx
<button type="button" onClick={() => openReportBuilder()}>
  Open Report Builder
</button>
```

Do not add the launch secret to Axios defaults. Those defaults go to **your** API with the **user** token. The secret is only on the Node → Report Builder call.

---

## Acceptance criteria

- Logged-in user clicks the button and lands in Report Builder **signed in**, without a Report Builder password.
- Browser Network: request to **your** `/api/report-builder/open` (with **your** custom token), then navigation to Report Builder `/sso?code=…`. **No** `X-Launch-Secret` and **no** `/api/auth/launch` from the browser.
- Logged-out user hitting open → **your** login, then continue.
- Second click works (new launch). Old codes are not reused.
- Crafted `?userName=admin` is ignored. Identity is `req.user` from your token.
- `returnPath=/insights` opens Insights after SSO.
- Secret is not in the React bundle, git, or HTML.

---

## Manual test plan

1. `GET {REPORT_BUILDER_API_BASE_URL}/api/auth/options` → `launchEnabled: true`.
2. Sign into **this** React app as a normal user.
3. Click **Open Report Builder**.
4. Land on Report Builder home (or `returnPath`). Sidebar: Reports / Analytics, not Users / Roles.
5. Sign out of Report Builder. Click again. New session.
6. DevTools → Network: confirm launch is not called from the browser.
7. Logged out of this app, call `/api/report-builder/open` → your 401, not Report Builder.

Typical URLs:

- This app (dev): existing Node + React
- Report Builder API (dev): `http://localhost:5244`
- Report Builder SPA (dev): `http://localhost:5173`
- Report Builder (production): `https://mecs.dmt.gov.ae/ReportBuilder`
- Launch (production): `POST https://mecs.dmt.gov.ae/ReportBuilder/api/auth/launch`
- SSO landing (production): `https://mecs.dmt.gov.ae/ReportBuilder/sso?code=…`

---

## Out of scope

- Changing Report Builder
- Converting this app to JWT
- Sharing or validating this app’s custom tokens inside Report Builder
- Syncing this app’s roles into Report Builder (first launch = Portal viewer only)
- Sending passwords or your token-signing keys to Report Builder
- Calling `/api/auth/exchange`, `/login`, or `/refresh` on Report Builder

---

## Handoff checklist

- [ ] Shared launch secret (same in both apps; server env only)
- [ ] Report Builder API base URL (dev + prod)
- [ ] Report Builder public SPA URL
- [ ] Where the React button lives
- [ ] Map from `req.user` after **your** token middleware: login, stable id, EN name, AR name, email
- [ ] Default `returnPath` (or omit)

---

## If something fails

| Symptom | Likely cause |
|---|---|
| 503 `Launch sign-in is not configured.` | Report Builder `Auth:Launch:Secret` empty. Restart RB API. |
| 401 from Report Builder `Launch secret is invalid.` | Secrets differ, whitespace, or header not sent from **Node**. |
| 401 from **your** API | Custom token middleware rejected the React call. User not logged into this app. |
| CORS on `5244/api/auth/launch` | React called Report Builder. Move launch to Node. |
| Secret visible in browser sources | You put it in `REACT_APP_` / `VITE_`. Move to server env. |
| Browser `/sso` “missing code” | You redirected to `/sso` without using `redirectUrl`. |
| Lands on Report Builder **login** | Open route never ran, or `/sso` exchange failed. |
| Wrong person | You trusted `userName` from the React request body/query. |
| Relative `/sso?code=…` | Set `Auth:Launch:PublicBaseUrl` on RB, or prefix with `REPORT_BUILDER_PUBLIC_URL`. |
