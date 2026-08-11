#!/usr/bin/env bash
# Sends one line to a human. Sourced by watchdog.sh; safe to run on its own to test the wiring:
#
#     ops/alert.sh "test mesajı"
#
# Why this exists: the watchdog already detects everything worth detecting, and then writes it to
# backups/watchdog.log — a file nobody opens. Its own comment says the failure mode is silence ("the
# cron breaks, nobody notices for months"), which is exactly what a log-only watchdog produces. A
# detector that reaches no one is a detector nobody has.
#
# Configuration lives in the environment, which under cron means /opt/attendanceqr/.env (read below,
# because cron gives a job almost no environment of its own). Set EITHER:
#
#   ALERT_TELEGRAM_TOKEN=123456:AA...     from @BotFather
#   ALERT_TELEGRAM_CHAT=-1001234567890    the group/channel id the bot was added to
#
# or a generic webhook, which is what Slack/Discord/Mattermost/n8n all speak:
#
#   ALERT_WEBHOOK_URL=https://...
#
# With neither set this is a NO-OP that says so in the log. That is deliberate: an unconfigured alert
# channel must never make the watchdog itself fail, or the monitoring takes the service down.
set -uo pipefail

APP_DIR=${APP_DIR:-/opt/attendanceqr}
ENV_FILE="$APP_DIR/.env"

# Pull only the alert keys out of .env. A plain `source` would drag in every secret the compose file
# uses and, worse, execute whatever is in there — and .env is CRLF, so the \r has to go or the token
# ends up with a carriage return glued to it and every send 400s.
if [ -f "$ENV_FILE" ]; then
  for key in ALERT_TELEGRAM_TOKEN ALERT_TELEGRAM_CHAT ALERT_WEBHOOK_URL; do
    if [ -z "${!key:-}" ]; then
      value=$(grep -E "^${key}=" "$ENV_FILE" 2>/dev/null | head -1 | cut -d= -f2- | tr -d '\r' || true)
      [ -n "$value" ] && export "$key=$value"
    fi
  done
fi

# The host, so an alert from three servers is not three identical messages.
ALERT_HOST=${ALERT_HOST:-$(hostname -s 2>/dev/null || echo qrlog)}

send_alert() {
  local text="🔴 QRLog [$ALERT_HOST] $*"

  if [ -n "${ALERT_TELEGRAM_TOKEN:-}" ] && [ -n "${ALERT_TELEGRAM_CHAT:-}" ]; then
    # --data-urlencode, not string interpolation: these messages carry container names, paths and
    # percent signs ("disk at 91%"), any of which would otherwise mangle the request.
    curl -fsS --max-time 15 -o /dev/null \
      "https://api.telegram.org/bot${ALERT_TELEGRAM_TOKEN}/sendMessage" \
      --data-urlencode "chat_id=${ALERT_TELEGRAM_CHAT}" \
      --data-urlencode "text=${text}" \
      && return 0
    echo "$(date -Is) alert: telegram send FAILED"
    return 1
  fi

  if [ -n "${ALERT_WEBHOOK_URL:-}" ]; then
    # {"text": …} is what Slack, Discord (with /slack), Mattermost and most webhook receivers accept.
    # jq is not installed on this box, so the string is escaped by hand — quotes and backslashes only,
    # which is all these messages can contain.
    local escaped
    escaped=$(printf '%s' "$text" | sed 's/\\/\\\\/g; s/"/\\"/g')
    curl -fsS --max-time 15 -o /dev/null -X POST "$ALERT_WEBHOOK_URL" \
      -H 'Content-Type: application/json' \
      -d "{\"text\":\"${escaped}\"}" \
      && return 0
    echo "$(date -Is) alert: webhook send FAILED"
    return 1
  fi

  echo "$(date -Is) alert: NOT SENT (no ALERT_TELEGRAM_* or ALERT_WEBHOOK_URL configured)"
  return 1
}

# Run directly rather than sourced → send the argument and exit. Lets the wiring be tested in one
# command instead of by breaking something.
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  send_alert "${*:-test}" && echo "sent"
fi
