#!/usr/bin/python3 -I
"""Proof, on the server, that GitHub approved this exact release (ops/CLOUD-DEPLOY.md).

Installed root-owned at /usr/local/sbin/qrlog-verify-release-token and run by the root release helper
(ops/prod-release.sh) for every request that arrives through the release key. The key alone proves
nothing: whoever holds it could ask for a deploy without anybody approving one. So the release job
also sends the OpenID Connect token GitHub issues to that job — and GitHub issues one with
`environment: production` only to a job that has passed the environment's protection rules, i.e.
after the required reviewer approved it, on main, from this workflow.

    qrlog-verify-release-token --audience qrlog-prod-deploy --sha <40-hex>   < token

Exit 0 and prints "<jti> <run_id>" when the token is GitHub's, unexpired, and names this repository,
main, the production environment, this workflow, a manual dispatch, the exact commit, and the mode (the
audience — a token from an approved preflight cannot start a deploy). Exit 1 with the failed check on
stderr otherwise. Nothing from the token is printed on failure: the output reaches a public workflow log.

Standard library only (the server has no third-party Python packages): RS256 is a modular
exponentiation and a byte comparison against the full expected PKCS#1 v1.5 encoding — no parsing of
the decrypted block, so no padding-oracle shortcuts.
"""

import argparse
import base64
import hashlib
import hmac
import json
import re
import sys
import time
import urllib.request

ISSUER = "https://token.actions.githubusercontent.com"
JWKS_URL = ISSUER + "/.well-known/jwks"

# Numeric ids as well as names: a renamed or re-created repository with the same name is not this one.
EXPECTED = {
    "repository": "HuseynovRuslan/AttendanceQR",
    "repository_id": "1287438011",
    "repository_owner_id": "155317262",
    "ref": "refs/heads/main",
    "ref_type": "branch",
    "environment": "production",
    "event_name": "workflow_dispatch",
    "workflow_ref": "HuseynovRuslan/AttendanceQR/.github/workflows/production-deploy.yml@refs/heads/main",
    "runner_environment": "github-hosted",
}
AUDIENCES = ("qrlog-prod-preflight", "qrlog-prod-deploy")
MAX_AGE_SECONDS = 600  # GitHub's tokens live 5 minutes; an older one is a replay, not a release
CLOCK_SKEW = 60
MAX_TOKEN_BYTES = 16384

# DER prefix of a SHA-256 DigestInfo (RFC 8017, section 9.2, note 1).
SHA256_DIGEST_INFO = bytes.fromhex("3031300d060960864801650304020105000420")


class Refused(Exception):
    pass


def b64url(part):
    if not re.fullmatch(r"[A-Za-z0-9_-]*", part):
        raise Refused("token is not base64url")
    return base64.urlsafe_b64decode(part + "=" * (-len(part) % 4))


def load_jwks(path):
    if path:
        with open(path, "rb") as f:
            return json.load(f)
    request = urllib.request.Request(JWKS_URL, headers={"User-Agent": "qrlog-release"})
    with urllib.request.urlopen(request, timeout=15) as response:  # TLS verified by default
        return json.load(response)


def rsa_key(jwks, kid):
    for key in jwks.get("keys", []):
        if key.get("kid") == kid and key.get("kty") == "RSA":
            n = int.from_bytes(b64url(key["n"]), "big")
            e = int.from_bytes(b64url(key["e"]), "big")
            if n.bit_length() < 2048:
                raise Refused("signing key is too short")
            return n, e
    raise Refused("token is not signed by a current GitHub Actions key")


def rs256_valid(n, e, signing_input, signature):
    k = (n.bit_length() + 7) // 8
    if len(signature) != k:
        return False
    s = int.from_bytes(signature, "big")
    if s >= n:
        return False
    encoded = pow(s, e, n).to_bytes(k, "big")
    digest_info = SHA256_DIGEST_INFO + hashlib.sha256(signing_input).digest()
    expected = b"\x00\x01" + b"\xff" * (k - len(digest_info) - 3) + b"\x00" + digest_info
    return hmac.compare_digest(encoded, expected)


def verify(token, audience, sha, jwks, now):
    if audience not in AUDIENCES:
        raise Refused("unknown audience")
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise Refused("malformed commit")
    parts = token.split(".")
    if len(parts) != 3:
        raise Refused("token is not a JWT")
    header = json.loads(b64url(parts[0]))
    if header.get("alg") != "RS256" or not isinstance(header.get("kid"), str):
        raise Refused("token is not RS256-signed")
    n, e = rsa_key(jwks, header["kid"])
    if not rs256_valid(n, e, (parts[0] + "." + parts[1]).encode("ascii"), b64url(parts[2])):
        raise Refused("token signature does not verify")

    claims = json.loads(b64url(parts[1]))
    if claims.get("iss") != ISSUER:
        raise Refused("token is not from GitHub Actions")
    aud = claims.get("aud")
    if aud != audience and not (isinstance(aud, list) and aud == [audience]):
        raise Refused("token was issued for another mode")
    for name in ("exp", "iat", "nbf"):
        if not isinstance(claims.get(name), int):
            raise Refused("token has no " + name)
    if claims["exp"] <= now:
        raise Refused("token has expired")
    if claims["nbf"] > now + CLOCK_SKEW or claims["iat"] > now + CLOCK_SKEW:
        raise Refused("token is not valid yet")
    if now - claims["iat"] > MAX_AGE_SECONDS:
        raise Refused("token is too old")
    for name, value in EXPECTED.items():
        if str(claims.get(name)) != value:
            raise Refused("token claim '" + name + "' is not this repository's production release")
    if "job_workflow_ref" in claims and claims["job_workflow_ref"] != EXPECTED["workflow_ref"]:
        raise Refused("token claim 'job_workflow_ref' is not this repository's production release")
    if claims.get("sha") != sha:
        raise Refused("token was issued for another commit")

    jti, run_id = str(claims.get("jti", "")), str(claims.get("run_id", ""))
    if not re.fullmatch(r"[A-Za-z0-9-]{8,64}", jti) or not re.fullmatch(r"[0-9]{1,20}", run_id):
        raise Refused("token has no usable id")
    return jti, run_id


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--audience", required=True)
    parser.add_argument("--sha", required=True)
    # For ops/tests/release-gate.test.sh. The helper passes neither; whoever can set them is root already.
    parser.add_argument("--jwks-file")
    parser.add_argument("--now", type=int)
    args = parser.parse_args()

    raw = sys.stdin.buffer.read(MAX_TOKEN_BYTES + 1)
    try:
        if len(raw) > MAX_TOKEN_BYTES:
            raise Refused("token is too long")
        token = raw.decode("ascii").strip()
        jwks = load_jwks(args.jwks_file)
        now = args.now if args.now is not None else int(time.time())
        jti, run_id = verify(token, args.audience, args.sha, jwks, now)
    except Refused as refused:
        print("approval proof refused: " + str(refused), file=sys.stderr)
        return 1
    except Exception as error:  # malformed JSON, network, anything: never a pass
        print("approval proof refused: could not verify (" + type(error).__name__ + ")", file=sys.stderr)
        return 1
    print(jti + " " + run_id)
    return 0


if __name__ == "__main__":
    sys.exit(main())
