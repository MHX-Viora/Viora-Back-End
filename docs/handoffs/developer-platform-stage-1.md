# Developer Platform — Stage 1

- Public `GET /api/developer/health`: HTTP 200, `{ "status": "ok", "service": "ankt-developer" }`, Cache-Control: no-store.
- `viora-BE/Developer/DeveloperEndpoints.cs` maps the endpoint from Program.cs. Explicit AllowAnonymous; no DB, auth/session business logic, DI services or migrations added.
- Existing JWT, CORS, logging, error handling and API routes are retained. Frontend server calls this endpoint without credentials; no new browser origin is needed.
- Frontend lives in the ANKT FE repository at `developer-portal/`, served separately behind the existing `https://mxh.ankt.vn/developers` prefix. The user selected the same domain instead of a new subdomain.
- Regression: 64 passed, 1 existing database audit skipped. Dedicated HTTP TestHost test also passed with fallback authorization requiring authentication, proving health remains public and returns only the intended fields.
- Retirement migration/model/controller tests still pass. Historical Mini App modules and data are not restored.
- No deployment or database write performed. Deploy through the existing BE release workflow, then verify `/api/developer/health` before routing the portal.
- Stage 2 requires an approved account/auth/authorization/data design. Do not infer that a successful liveness response enables Developer accounts or Mini App functionality.
