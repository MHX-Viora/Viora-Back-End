# Domain verification file at website root

Verifier now fetches https://<host>/ankt-mini-app-verification.txt. Domain security checks remain unchanged: public DNS/socket pinning, HTTPS, no redirects, exact token, response-size and timeout limits.

Deploy with the updated developer/admin portals. Move existing challenge files from the old subdirectory to the website root before re-verifying. Existing verification records are retained.

Validation: 50 MiniApp tests passed; one optional database test skipped. No deployment performed.
