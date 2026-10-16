#!/usr/bin/env python3
"""App Store Connect API (Build Uploads) ile .ipa yükler — altool gerekmez.

Kullanım: asc_upload.py <ipa|pkg> <app_id> <kısa sürüm> <yapı no> [IOS|MAC_OS]
Ortam: ASC_KEY_ID, ASC_ISSUER_ID, KEY (.p8 yolu)
"""
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request

import jwt  # pyjwt

API = "https://api.appstoreconnect.apple.com/v1"


def token():
    now = int(time.time())
    return jwt.encode(
        {"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 1100, "aud": "appstoreconnect-v1"},
        open(os.environ["KEY"]).read(),
        algorithm="ES256",
        headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"},
    )


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, data=json.dumps(body).encode() if body is not None else None)
    req.add_header("Authorization", "Bearer " + token())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as r:
            txt = r.read().decode()
            return json.loads(txt) if txt else {}
    except urllib.error.HTTPError as e:
        print(f"HATA {method} {path}: {e.code}\n{e.read().decode()}")
        sys.exit(1)


def main():
    ipa, app_id, short, build = sys.argv[1:5]
    platform = sys.argv[5] if len(sys.argv) > 5 else "IOS"
    uti = "com.apple.pkg" if ipa.endswith(".pkg") else "com.apple.ipa"
    data = open(ipa, "rb").read()
    print(f"ipa: {len(data)} bayt, sürüm {short} ({build})")

    # kimlik doğrulama denetimi
    app = call("GET", f"/apps/{app_id}")
    print("uygulama:", app["data"]["attributes"].get("name"), app["data"]["attributes"].get("bundleId"))

    up = call("POST", "/buildUploads", {"data": {
        "type": "buildUploads",
        "attributes": {"cfBundleShortVersionString": short, "cfBundleVersion": build, "platform": platform},
        "relationships": {"app": {"data": {"type": "apps", "id": app_id}}},
    }})
    up_id = up["data"]["id"]
    print("buildUpload:", up_id)

    f = call("POST", "/buildUploadFiles", {"data": {
        "type": "buildUploadFiles",
        "attributes": {"assetType": "ASSET", "fileName": os.path.basename(ipa), "fileSize": len(data), "uti": uti},
        "relationships": {"buildUpload": {"data": {"type": "buildUploads", "id": up_id}}},
    }})
    file_id = f["data"]["id"]
    ops = f["data"]["attributes"].get("uploadOperations") or []
    print("buildUploadFile:", file_id, "parça:", len(ops))

    for i, op in enumerate(ops):
        off, ln = op.get("offset", 0), op.get("length", len(data))
        req = urllib.request.Request(op["url"], method=op.get("method", "PUT"), data=data[off:off + ln])
        for h in op.get("requestHeaders") or []:
            req.add_header(h["name"], h["value"])
        try:
            with urllib.request.urlopen(req) as r:
                print(f"parça {i + 1}/{len(ops)}: {r.status}")
        except urllib.error.HTTPError as e:
            print(f"parça {i + 1} HATA {e.code}: {e.read().decode()[:2000]}")
            sys.exit(1)

    call("PATCH", f"/buildUploadFiles/{file_id}", {"data": {
        "type": "buildUploadFiles", "id": file_id,
        "attributes": {"uploaded": True, "sourceFileChecksums": {"file": {"hash": hashlib.md5(data).hexdigest(), "algorithm": "MD5"}}},
    }})
    print("yükleme tamamlandı bildirildi")

    # Apple işlemeyi bitirene kadar izle: reddedilen yapı (ör. 90296 sandbox) CI'da hata olarak görünsün
    for _ in range(60):
        state = call("GET", f"/buildUploads/{up_id}")["data"]["attributes"].get("state") or {}
        s = state.get("state") if isinstance(state, dict) else str(state)
        print("durum:", s)
        if s == "COMPLETE":
            return
        if s == "FAILED":
            for e in state.get("errors") or []:
                print(f"::error title=App Store {e.get('code')}::{e.get('description')}")
            sys.exit(1)
        time.sleep(20)
    print("::warning::Apple işlemeyi 20 dakikada bitirmedi; sonucu ios.yml 'durum' ile denetle")


if __name__ == "__main__":
    main()
