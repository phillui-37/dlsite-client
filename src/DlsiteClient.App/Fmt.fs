module DlsiteClient.App.Fmt

open System

/// AOT-safe formatting — never use F# sprintf/printf (PrintfImpl uses reflection).
let fmt0 (s: string) = s
let fmt1 (template: string) (a0: obj) = String.Format(template, a0)
let fmt2 (template: string) (a0: obj) (a1: obj) = String.Format(template, a0, a1)
let fmt3 (template: string) (a0: obj) (a1: obj) (a2: obj) = String.Format(template, a0, a1, a2)
let fmt4 (template: string) (a0: obj) (a1: obj) (a2: obj) (a3: obj) =
    String.Format(template, a0, a1, a2, a3)
let fmt5 (template: string) (a0: obj) (a1: obj) (a2: obj) (a3: obj) (a4: obj) =
    String.Format(template, a0, a1, a2, a3, a4)
let fmt6 (template: string) (a0: obj) (a1: obj) (a2: obj) (a3: obj) (a4: obj) (a5: obj) =
    String.Format(template, a0, a1, a2, a3, a4, a5)
let fmt7 (template: string) (a0: obj) (a1: obj) (a2: obj) (a3: obj) (a4: obj) (a5: obj) (a6: obj) =
    String.Format(template, a0, a1, a2, a3, a4, a5, a6)
let fmt8 (template: string) (a0: obj) (a1: obj) (a2: obj) (a3: obj) (a4: obj) (a5: obj) (a6: obj) (a7: obj) =
    String.Format(template, a0, a1, a2, a3, a4, a5, a6, a7)

let sizeBytes (bytes: int64) =
    if bytes <= 0L then "-"
    elif bytes >= (1L <<< 30) then fmt1 "{0:0.00} GB" (float bytes / 1e9)
    elif bytes >= (1L <<< 20) then fmt1 "{0:0.00} MB" (float bytes / 1e6)
    else fmt1 "{0:0} KB" (float bytes / 1e3)

let sizeBps (bps: float) =
    if bps >= 1e6 then fmt1 "{0:0.00} Mbps" (bps * 8.0 / 1e6)
    elif bps >= 1e3 then fmt1 "{0:0} Kbps" (bps * 8.0 / 1e3)
    else "0"

let sizePlain (b: int64) =
    if b >= (1L <<< 30) then fmt1 "{0:0.00} GB" (float b / 1e9)
    elif b >= (1L <<< 20) then fmt1 "{0:0.00} MB" (float b / 1e6)
    else fmt1 "{0} B" b
