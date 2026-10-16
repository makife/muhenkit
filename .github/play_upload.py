"""Google Play'e AAB yükler (Android Publisher API v3, hizmet hesabı).

  python play_upload.py <hizmet_hesabi.json> <paket> <aab|-> <kanal> [durum]

aab yerine "-" verilirse yalnızca erişim denetlenir ve kanallar listelenir (hiçbir şey değişmez).
kanal: internal | alpha | beta | production. durum: completed (varsayılan) ya da draft
(uygulama henüz yayınlanmamışsa Play yalnız draft kabul eder; betik bunu görünce draft ile yeniden dener).
"""
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

import jwt

API = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/"
UPLOAD = "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/"


def token(sa):
    now = int(time.time())
    assertion = jwt.encode({"iss": sa["client_email"], "scope": "https://www.googleapis.com/auth/androidpublisher",
                            "aud": sa["token_uri"], "iat": now, "exp": now + 3600}, sa["private_key"], algorithm="RS256")
    body = urllib.parse.urlencode({"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer", "assertion": assertion}).encode()
    return json.load(urllib.request.urlopen(urllib.request.Request(sa["token_uri"], body)))["access_token"]


def call(tok, method, url, body=None, data=None, ctype="application/json"):
    if body is not None:
        data = json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": "Bearer " + tok, "Content-Type": ctype})
    try:
        with urllib.request.urlopen(req, timeout=600) as r:
            raw = r.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"{method} {url.split('/applications/')[-1]} → {e.code}: {e.read().decode(errors='replace')}") from None


def main():
    sa_path, pkg, aab, track = sys.argv[1:5]
    status = sys.argv[5] if len(sys.argv) > 5 else "completed"
    sa = json.load(open(sa_path, encoding="utf-8"))
    tok = token(sa)
    base = API + pkg + "/edits"
    edit = call(tok, "POST", base, {})["id"]
    try:
        if aab == "-":
            for t in call(tok, "GET", f"{base}/{edit}/tracks").get("tracks", []):
                print("kanal:", t["track"], [(r.get("versionCodes"), r.get("status")) for r in t.get("releases", [])])
            print("Erişim tamam:", sa["client_email"])
            return
        with open(aab, "rb") as f:
            vc = call(tok, "POST", f"{UPLOAD}{pkg}/edits/{edit}/bundles?uploadType=media", data=f.read(),
                      ctype="application/octet-stream")["versionCode"]
        print("Yüklendi: versionCode", vc)
        for st in ([status, "draft"] if status != "draft" else ["draft"]):
            call(tok, "PUT", f"{base}/{edit}/tracks/{track}",
                 {"track": track, "releases": [{"versionCodes": [str(vc)], "status": st}]})
            try:
                call(tok, "POST", f"{base}/{edit}:commit")
                print(f"Kanal '{track}' → {st}")
                return
            except RuntimeError as e:
                if "draft" in str(e).lower() and st != "draft":
                    print("Uygulama henüz yayınlanmamış: taslak olarak yeniden deneniyor")
                    continue
                raise
    finally:
        try:
            call(tok, "DELETE", f"{base}/{edit}")
        except RuntimeError:
            pass  # commit edilen düzenleme zaten kapanmıştır


if __name__ == "__main__":
    main()
