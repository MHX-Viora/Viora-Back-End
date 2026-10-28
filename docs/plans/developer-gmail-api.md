# Gmail API implementation

1. Add focused failing HTTP-transport/readiness tests for Gmail sender.
2. Add options, fixed-endpoint refresh-token exchange and MIME messages.send.
   Preserve SMTP default; wire provider choice through existing DI boundary.
3. Run sender + existing identity/API tests, build, review security/diff.
4. Document Google grant, Render environment, token expiry/recovery and handoff.

No changes to unrelated in-progress developer platform work. Acceptance and
commands: ../specs/developer-gmail-api.md. Google credentials are an external
deployment prerequisite, not a reason to stop implementation.

Completed: sender/options/DI, tests and operational docs. Focused suite: 33/33;
backend built successfully. Remaining: owner Google grant, Render secret setup,
deployment and real inbox verification. See ../handoffs/developer-gmail-api.md.
