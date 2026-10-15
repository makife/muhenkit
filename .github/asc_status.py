#!/usr/bin/env python3
"""App Store Connect: son yüklemeler ve yapıların işleme durumu (teşhis)."""
import json, os, sys, time, urllib.error, urllib.request
import jwt

API = "https://api.appstoreconnect.apple.com/v1"
APP = sys.argv[1]

def token():
    now = int(time.time())
    return jwt.encode({"iss": os.environ["ASC_ISSUER_ID"], "iat": now, "exp": now + 1100, "aud": "appstoreconnect-v1"},
                      open(os.environ["KEY"]).read(), algorithm="ES256", headers={"kid": os.environ["ASC_KEY_ID"], "typ": "JWT"})

def get(path):
    req = urllib.request.Request(API + path)
    req.add_header("Authorization", "Bearer " + token())
    try:
        with urllib.request.urlopen(req) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        print(f"HATA GET {path}: {e.code} {e.read().decode()[:1500]}")
        return {}

print("== Yapılar (builds) ==")
for b in get(f"/builds?filter[app]={APP}&sort=-uploadedDate&limit=10").get("data", []):
    a = b["attributes"]
    print(a.get("version"), a.get("processingState"), a.get("uploadedDate"), "expired" if a.get("expired") else "",
          "şifreleme:", a.get("usesNonExemptEncryption"))

print("== Yüklemeler (buildUploads) ==")
for u in get(f"/apps/{APP}/buildUploads?limit=10").get("data", []) or get(f"/buildUploads?filter[app]={APP}&limit=10").get("data", []):
    a = u["attributes"]
    print(a.get("cfBundleVersion"), a.get("cfBundleShortVersionString"), json.dumps(a.get("state"), ensure_ascii=False)[:1500])
