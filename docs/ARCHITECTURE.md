# Architecture

## Solution

| Project | Lang | Role |
|---------|------|------|
| `DlsiteClient.App` | F# | Avalonia.FuncUI Elmish UI |
| `DlsiteClient.Core` | C# | API, DB, queue, extract, launcher |
| `DlsiteClient.Platform` | C# | Abstractions |
| `DlsiteClient.Platform.Windows` | C# | Creds, Nest import, protocol, JP locale, startup |
| `DlsiteClient.Platform.Linux` | C# | Stub until M5 |

## Data flow

```
UI (FuncUI) → Core services → Platform traits
Auth → Avalonia WebAuthenticationBroker OIDC (`dlsite.user` / fragment `/home/login`) → POST nest/login → CredentialStore
Library ← GET purchases (+ detail / product.json enrich)
Queue → Range download → cache → 7z extract → library folder
```

## Local state

SQLite under app data: purchases cache, fav/hide, install paths, exe choice, queue order, update_date.

## Secrets

`__DLsite_SID` / tokens in OS credential store only.

## Download / extract isolation

- Background workers + channels; UI observes events only
- External `7z` process for extract/test
- Bounded concurrency; job failures do not kill process

## Logging

Microsoft.Extensions.Logging + file sink with fixed rotation; user sets level + directory only.
