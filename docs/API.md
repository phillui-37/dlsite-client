# API notes

Base artifact: [`openapi.yaml`](../openapi.yaml) (Nest v2.0.27 doc; install may be newer — re-diff when needed).

## Auth

1. OIDC via `https://login.dlsite.com`
2. `POST /home/api/native-app/nest/login` with Authorization id token + form `state`, `session_state`
3. Cookie `__DLsite_SID=<session_id>;` on subsequent calls
4. `DELETE` same path to logout
5. User-Agent: `DLsiteNest/<version>` (work types uses `DLSiteNest`)

## Library

- `GET /home/api/native-app/nest/purchases?locale&page` → `Product[]`
- Useful fields: `workno`, `work_name`, `maker_name`, `work_type`, `contents_file_size`, `point`, `regist_date`, `sales_date`, `update_date`, `image_urls`, `tags` (shape TBD — probe M1), `site_id`, `floor_id`, `age_category`

## Detail / download URLs

- `GET /home/api/native-app/nest/product/detail?workno&locale&is_ana&fields`
- `contents_with_url[].url` for binary download (cookies required)
- Summary: `/{siteId}/user/product/summary/=/products/{workId}` → `down_url`

## Work types / updates

- `GET /home/api/native-app/nest/all/work/types?locale`
- Content revisions: `POST /maniax/product/revision/ajax`
- Nest app update: `GET /home/api/native-app/nest/update/info` (client app, not work updates)

## Tags (probe status)

- Nest `Product.tags`: untyped in OpenAPI — on sync, shape is logged once (`LibraryService`); normalized to `string[]` JSON when array/objects-with-`name`
- Fallback: `GET https://www.dlsite.com/api/=/product.json?workno=` → `genres` / `genre` (sample of first 5 works after sync)
- HTML scrape: not wired in v1; geo-block soft-fail already on `product.json` text heuristics

## DLC (probe status)

- Nest OpenAPI: no parent/DLC fields
- Runtime probe on sample works via `product.json`: `is_pack_child` / `is_bonus_work` + `work_pack_parent`
- If probe succeeds at least once → `LibraryService.DlcFeatureEnabled` and UI badge; else feature stays off (no scrape)

## Binary download

- Not in OpenAPI; Range resume client-side; cookie/session required
- Domains from Nest `appsettings`: ApiDomain, DownloadDomain, LoginDomain, …

## Locale query

- Nest: `ja_JP`, `en_US`, … (underscore). Map from UI `ja` / `zh-TW`.
