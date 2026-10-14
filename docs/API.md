# API notes

Nest HTTP surface (from Nest client reverse-engineering). Re-diff Nest install when DLsite updates the app.

## Auth (Nest LoginWindow OIDC)

Exact flow from Nest `LoginWindow` (WebView2):

1. Navigate to:
   ```
   https://login.dlsite.com/oauth2/auth
     ?lang=ja|zh_TW|…
     &response_type=id_token
     &response_mode=fragment
     &client_id=dlsite.user
     &scope=openid
     &redirect_uri=https://www.dlsite.com/home/login
     &nonce=<6 alnum>
     &state=<12 alnum>
   ```
2. On redirect `Location` starting with `https://www.dlsite.com/home/login#…`, parse fragment:
   - `id_token`, `state`, `session_state`
3. `POST https://www.dlsite.com/home/api/native-app/nest/login`
   - Header `Authorization: <id_token>` (raw token, not `Bearer`)
   - Form: `state`, `session_state`
   - UA: `DLsiteNest/<version>`
4. Response `data.session_id` → cookie/`__DLsite_SID` for subsequent calls; store in OS credentials
5. `DELETE` same path to logout

Client: Avalonia `WebAuthenticationBroker` (`Avalonia.Controls.WebView` 12.1.x MIT) + `NestOidc` / `AuthSession.LoginWithOidcAsync`. Manual SID paste remains as debug fallback.

**Session expiry (M6):** authed Nest calls treat HTTP 401/403 (and JSON `meta.code` 401/403) as expired SID → clear local credentials, pause download queue, prompt re-login. Binary download URLs use the same HTTP status check.

## Library

- `GET /home/api/native-app/nest/purchases?locale&page` → `Product[]`
- Useful fields: `workno`, `work_name`, `maker_name`, `work_type`, `contents_file_size`, `point`, `regist_date`, `sales_date`, `update_date`, `image_urls`, `tags` (untyped), `site_id`, `floor_id`, `age_category`

## Detail / download URLs

- `GET /home/api/native-app/nest/product/detail?workno&locale&is_ana&fields`
- `contents_with_url[].url` for binary download (cookies required)
- Summary: `/{siteId}/user/product/summary/=/products/{workId}` → `down_url`

## Work types / updates

- `GET /home/api/native-app/nest/all/work/types?locale`
- Content revisions: `POST /maniax/product/revision/ajax`
- Nest app update: `GET /home/api/native-app/nest/update/info` (client app, not work updates)

## Tags (probe)

- Nest `Product.tags`: untyped in OpenAPI — on sync, shape logged once (`LibraryService`); normalized to `string[]` JSON when array / objects-with-`name`
- Fallback sample (first 5 works): `GET https://www.dlsite.com/api/=/product.json?workno=` → `genres` / `genre`
- Geo-block: soft-fail on product.json text heuristics; no HTML scrape in v1

## DLC (probe)

- Nest OpenAPI: no parent/DLC fields
- Runtime probe on sample works via `product.json`: `is_pack_child` / `is_bonus_work` + `work_pack_parent`
- If probe succeeds at least once → `LibraryService.DlcFeatureEnabled` and UI badge; else feature stays off

## Binary download

- Not in OpenAPI; Range resume client-side; cookie/session required
- Domains from Nest `appsettings`: ApiDomain, DownloadDomain, LoginDomain, …

## Locale query

- Nest API: `ja_JP`, `zh_TW`, `en_US`, … (underscore). Map from UI `ja` / `zh-TW`.
- OIDC `lang`: Nest uses short codes (`ja`, …) via `LanguageEnum.GetLoginCode`
