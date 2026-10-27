# HTML email plan

1. Write failing render/MIME tests; inspect existing token and notification calls.
2. Add content overload, embedded HTML renderer and shared MIME factory; connect
   only verification and reset email triggers already present.
3. Verify render/encoding/failure logging + existing tests; build backend.
4. Generate synthetic previews from renderer; inspect desktop/mobile, document
   actual evidence, compatibility limits and future template extension path.

Preserve unrelated dirty work and prior no-doc/log-commit preference; no commit
or external mail send unless a recipient is explicitly authorized.

Completed: renderer/layout, MIME integration, focused tests 49/49, build and
Chromium desktop/mobile inspection. Full suite 792 passed / 80 failed / 1 skipped;
isolated fixture errors documented in ../handoffs/developer-email-html.md.
Owner chose to perform real Gmail delivery/client verification later.
