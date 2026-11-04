"""Tests for ops/verify-release-token.py — run by ops/tests/release-gate.test.sh.

Mints real RS256 tokens with a throwaway key (openssl), then checks that exactly the token GitHub would
issue to an approved production job of this repository passes, and every near miss is refused.
Prints one line per failure and exits non-zero if there was any.
"""

import base64
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
VERIFIER = os.path.join(HERE, "..", "verify-release-token.py")
SHA = "0123456789abcdef0123456789abcdef01234567"
NOW = 1_800_000_000
failures = []
passed = 0


def b64(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def openssl(*args, data=None):
    return subprocess.run(["openssl", *args], input=data, capture_output=True, check=True).stdout


def new_key(directory, name):
    path = os.path.join(directory, name + ".pem")
    openssl("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", path)
    modulus = openssl("rsa", "-in", path, "-noout", "-modulus").decode().strip().split("=", 1)[1]
    return path, {"kty": "RSA", "alg": "RS256", "use": "sig", "kid": name,
                  "n": b64(bytes.fromhex(modulus)), "e": b64((65537).to_bytes(3, "big"))}


def claims(**overrides):
    c = {
        "jti": "6f1c0a4e-1b2c-4d5e-8f90-123456789abc",
        "sub": "repo:HuseynovRuslan/AttendanceQR:environment:production",
        "aud": "qrlog-prod-deploy",
        "iss": "https://token.actions.githubusercontent.com",
        "repository": "HuseynovRuslan/AttendanceQR",
        "repository_id": "1287438011",
        "repository_owner": "HuseynovRuslan",
        "repository_owner_id": "155317262",
        "ref": "refs/heads/main",
        "ref_type": "branch",
        "sha": SHA,
        "environment": "production",
        "event_name": "workflow_dispatch",
        "workflow_ref": "HuseynovRuslan/AttendanceQR/.github/workflows/production-deploy.yml@refs/heads/main",
        "job_workflow_ref": "HuseynovRuslan/AttendanceQR/.github/workflows/production-deploy.yml@refs/heads/main",
        "runner_environment": "github-hosted",
        "run_id": "12345678901",
        "iat": NOW - 20,
        "nbf": NOW - 620,
        "exp": NOW + 280,
    }
    for name, value in overrides.items():
        if value is None:
            c.pop(name, None)
        else:
            c[name] = value
    return c


def mint(key_path, kid, body, alg="RS256"):
    header = b64(json.dumps({"alg": alg, "kid": kid, "typ": "JWT"}).encode())
    payload = b64(json.dumps(body).encode())
    signing_input = (header + "." + payload).encode()
    signature = openssl("dgst", "-sha256", "-sign", key_path, data=signing_input)
    return header + "." + payload + "." + b64(signature)


def run(token, jwks_file, audience="qrlog-prod-deploy", sha=SHA):
    return subprocess.run(
        [sys.executable, "-I", VERIFIER, "--audience", audience, "--sha", sha,
         "--jwks-file", jwks_file, "--now", str(NOW)],
        input=token.encode(), capture_output=True)


def expect(name, result, accepted):
    global passed
    ok = (result.returncode == 0) == accepted
    if ok and not accepted:
        err = result.stderr.decode()
        # A refusal names the failed check only — nothing from the token reaches the public log.
        ok = err.startswith("approval proof refused:") and SHA not in err and result.stdout == b""
    if ok:
        passed += 1
    else:
        failures.append(name + " (exit " + str(result.returncode) + ", " + result.stderr.decode().strip() + ")")


def main():
    with tempfile.TemporaryDirectory() as d:
        key, jwk = new_key(d, "github-key")
        other_key, other_jwk = new_key(d, "other-key")
        jwks_file = os.path.join(d, "jwks.json")
        with open(jwks_file, "w") as f:
            json.dump({"keys": [jwk]}, f)

        good = mint(key, "github-key", claims())
        result = run(good, jwks_file)
        expect("the approved deploy token passes", result, True)
        if result.returncode == 0 and result.stdout.decode().split() != ["6f1c0a4e-1b2c-4d5e-8f90-123456789abc", "12345678901"]:
            failures.append("a passing token prints '<jti> <run_id>'")
        expect("an approved preflight token passes as a preflight",
               run(mint(key, "github-key", claims(aud="qrlog-prod-preflight")), jwks_file, "qrlog-prod-preflight"), True)
        expect("a token without job_workflow_ref still passes on workflow_ref",
               run(mint(key, "github-key", claims(job_workflow_ref=None)), jwks_file), True)

        refused = {
            "a preflight token used for a deploy": claims(aud="qrlog-prod-preflight"),
            "another audience": claims(aud="sts.amazonaws.com"),
            "another commit": claims(sha="f" * 40),
            "another branch": claims(ref="refs/heads/ops/claude-cloud-deploy"),
            "a tag": claims(ref="refs/tags/v1", ref_type="tag"),
            "another environment": claims(environment="staging"),
            "no environment (a job that skipped approval)": claims(environment=None),
            "a push, not a manual dispatch": claims(event_name="push"),
            "a pull request": claims(event_name="pull_request"),
            "a repository with the same name but another id": claims(repository_id="1"),
            "another owner id": claims(repository_owner_id="1"),
            "another repository": claims(repository="someone/AttendanceQR"),
            "another workflow": claims(workflow_ref="HuseynovRuslan/AttendanceQR/.github/workflows/ci.yml@refs/heads/main",
                                       job_workflow_ref=None),
            "a reusable workflow from elsewhere": claims(job_workflow_ref="evil/repo/.github/workflows/x.yml@refs/heads/main"),
            "a self-hosted runner": claims(runner_environment="self-hosted"),
            "another issuer": claims(iss="https://example.com"),
            "an expired token": claims(exp=NOW - 1),
            "a token issued too long ago": claims(iat=NOW - 700, nbf=NOW - 1300, exp=NOW + 60),
            "a token not valid yet": claims(nbf=NOW + 300),
            "a token issued in the future": claims(iat=NOW + 300),
            "no expiry": claims(exp=None),
            "no token id": claims(jti=None),
            "no run id": claims(run_id=None),
        }
        for name, body in refused.items():
            expect(name + " is refused", run(mint(key, "github-key", body), jwks_file), False)

        header, payload, signature = good.split(".")
        forged_payload = b64(json.dumps(claims(sha="f" * 40)).encode())
        expect("a payload changed after signing is refused",
               run(header + "." + forged_payload + "." + signature, jwks_file, sha="f" * 40), False)
        expect("a token signed by an unknown key is refused",
               run(mint(other_key, "other-key", claims()), jwks_file), False)
        expect("a token signed by another key under GitHub's key id is refused",
               run(mint(other_key, "github-key", claims()), jwks_file), False)
        none_header = b64(json.dumps({"alg": "none", "kid": "github-key"}).encode())
        expect("alg none is refused", run(none_header + "." + payload + ".", jwks_file), False)
        expect("alg HS256 is refused", run(mint(key, "github-key", claims(), alg="HS256"), jwks_file), False)
        expect("an empty token is refused", run("", jwks_file), False)
        expect("garbage is refused", run("not.a.token", jwks_file), False)
        expect("a newline-joined pair of tokens is refused", run(good + "\n" + good, jwks_file), False)
        expect("an oversized token is refused", run("a" * 20000, jwks_file), False)
        expect("a malformed --sha is refused", run(good, jwks_file, sha="0123"), False)
        expect("an unknown audience is refused", run(good, jwks_file, audience="qrlog-prod-shell"), False)
        with open(os.path.join(d, "empty.json"), "w") as f:
            json.dump({"keys": []}, f)
        expect("no keys at all is refused", run(good, os.path.join(d, "empty.json")), False)
        with open(os.path.join(d, "short.json"), "w") as f:
            short = dict(jwk, n=b64(bytes.fromhex("c3" * 128)))
            json.dump({"keys": [short]}, f)
        expect("a short signing key is refused", run(good, os.path.join(d, "short.json")), False)

    for failure in failures:
        print("FAIL: verifier — " + failure)
    print("verifier: %d passed, %d failed" % (passed, len(failures)))
    return 1 if failures or passed == 0 else 0


if __name__ == "__main__":
    sys.exit(main())
