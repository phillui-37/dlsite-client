# Milestones

| ID | Scope | Status |
|----|--------|--------|
| M0 | Docs SSoT + FuncUI F# / C# Core skeleton + Native AOT smoke gate | done (pushed a549dba) |
| M1 | Auth + purchases sync + Nest path import; probe tags + product.json DLC | done (OIDC WebView2 + Nest path confirm) |
| M2 | Library UI (3 modes, filters, cols, search, detail) + i18n + tags/DLC if proven | done |
| M3 | Download queue, multi-select DL/update, resume, extract triage, rate limit, proxy, isolation | done (Up Next reorder, live slots/proxy, extract triage UX) |
| M4 | Launcher, protocol, locale, settings (log/proxy/rate), fav/hide, multi-select uninstall | done (protocol→picker, change exe, library Play, live log) |
| M4.1 | Library perf/UX: off-UI IO, async thumbs, paging 20/50/100, collapsed filters, live settings | done |
| M5 | Linux + keyring (libsecret) + Proton/Wine launch + **AppImage** packaging | done |
| M6 | Session expiry: detect Nest 401/auth fail, clear SID, prompt re-login, pause queue | done |

## LM Studio

Optional for boilerplate only (`localhost:1234`). Auth, download correctness, architecture stay human/agent-owned.
