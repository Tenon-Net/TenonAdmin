---
name: configure-notifications
description: "[OMX] Configure OMX notifications - unified entry point for all platforms"
triggers:
  - "configure notifications"
  - "setup notifications"
  - "notification settings"
  - "configure discord"
  - "configure telegram"
  - "configure slack"
  - "configure openclaw"
  - "setup discord"
  - "setup telegram"
  - "setup slack"
  - "setup openclaw"
  - "discord notifications"
  - "telegram notifications"
  - "slack notifications"
  - "openclaw notifications"
  - "discord webhook"
  - "telegram bot"
  - "slack webhook"
---

# Configure OMX Notifications

Use this card for Discord, Telegram, Slack, generic command aliases, or OpenClaw. Shared safety, lifecycle, hook, team, autonomy, and cancellation rules live in [AGENTS.md](../../../AGENTS.md).

## 1. Inspect and choose a path

```bash
CONFIG_FILE="$HOME/.codex/.omx-config.json"
if [ -f "$CONFIG_FILE" ]; then
  jq -r '{notifications_enabled:(.notifications.enabled // false), discord:(.notifications.discord.enabled // false), discord_bot:(.notifications["discord-bot"].enabled // false), telegram:(.notifications.telegram.enabled // false), slack:(.notifications.slack.enabled // false), openclaw:(.notifications.openclaw.enabled // false), custom_webhook_command:(.notifications.custom_webhook_command.enabled // false), custom_cli_command:(.notifications.custom_cli_command.enabled // false), verbosity:(.notifications.verbosity // "session"), idleCooldownSeconds:(.notifications.idleCooldownSeconds // 60), reply_enabled:(.notifications.reply.enabled // false)}' "$CONFIG_FILE"
else
  echo "NO_CONFIG_FILE"
fi
```

Use the user's specified provider; otherwise use an available structured question tool to choose: Discord native (webhook or bot), Telegram native (bot token + chat id), Slack native (incoming webhook), `custom_webhook_command`, `custom_cli_command`, cross-cutting settings, or disable all notifications.

Configuration and test delivery are separate actions: send a notification only when the user authorized sending to that exact destination. Otherwise validate locally and report delivery as untested.

## 2. Native providers

Collect and validate provider values, then write only these native keys: `notifications.discord`, `notifications["discord-bot"]`, `notifications.telegram`, or `notifications.slack`. Do not represent native providers with generic aliases.

## 3. Generic aliases

For `custom_webhook_command`, collect URL, optional headers, method (`POST` default or `PUT`), events (`session-end`, `ask-user-question`, `session-start`, `session-idle`, `stop`), and an instruction template:

```bash
jq --arg url "$URL" --arg method "${METHOD:-POST}" --arg instruction "${INSTRUCTION:-OMX event {{event}} for {{projectPath}}}" --argjson headers "${HEADERS_JSON:-{}}" '.notifications=(.notifications//{enabled:true})|.notifications.enabled=true|.notifications.custom_webhook_command={enabled:true,url:$url,headers:$headers,method:$method,instruction:$instruction,events:["session-end","ask-user-question"]}' "$CONFIG_FILE" >"$CONFIG_FILE.tmp" && mv "$CONFIG_FILE.tmp" "$CONFIG_FILE"
```

For `custom_cli_command`, collect a command template (`{{event}}`, `{{instruction}}`, `{{sessionId}}`, `{{projectPath}}`), optional events, and an instruction template:

```bash
jq --arg command "$COMMAND_TEMPLATE" --arg instruction "${INSTRUCTION:-OMX event {{event}} for {{projectPath}}}" '.notifications=(.notifications//{enabled:true})|.notifications.enabled=true|.notifications.custom_cli_command={enabled:true,command:$command,instruction:$instruction,events:["session-end","ask-user-question"]}' "$CONFIG_FILE" >"$CONFIG_FILE.tmp" && mv "$CONFIG_FILE.tmp" "$CONFIG_FILE"
```

OpenClaw dispatch requires `OMX_OPENCLAW=1`; command gateways additionally require `OMX_OPENCLAW_COMMAND=1`; timeout precedence is `gateways.<name>.timeout` > `OMX_OPENCLAW_COMMAND_TIMEOUT_MS` > `5000` ms.

## 4. OpenClaw + Clawdbot agent mode

Use this only when the requested route is a `clawdbot agent` turn delivered to Discord. The `session-stop` notification intentionally maps to the OpenClaw `stop` hook. OMX shell-escapes substitutions; keep instructions short, avoid untrusted shell metacharacters, append `|| true`, and append JSONL logs with `>>`.

```bash
jq --arg command "(clawdbot agent --session-id omx-hooks --message {{instruction}} --thinking minimal --deliver --reply-channel discord --reply-to 'channel:CHANNEL_ID' --timeout 120 --json >>/tmp/omx-openclaw-agent.jsonl 2>&1 || true)" '.notifications=(.notifications//{enabled:true})|.notifications.enabled=true|.notifications.verbosity="verbose"|.notifications.openclaw={enabled:true,gateways:{local:{type:"command",command:$command,timeout:120000}},hooks:{"session-start":{enabled:true,gateway:"local",instruction:"OMX hook=session-start project={{projectName}} session={{sessionId}} tmux={{tmuxSession}}. 한국어로 상태를 #omc-dev에 공유하고 SOUL.md를 참고하세요."},"session-idle":{enabled:true,gateway:"local",instruction:"OMX hook=session-idle session={{sessionId}} tmux={{tmuxSession}}. 한국어로 팔로업을 #omc-dev에 안내하세요."},"ask-user-question":{enabled:true,gateway:"local",instruction:"OMX hook=ask-user-question session={{sessionId}} tmux={{tmuxSession}} question={{question}}. 한국어로 응답 필요를 #omc-dev에 알리세요."},"stop":{enabled:true,gateway:"local",instruction:"OMX hook=session-stop session={{sessionId}} tmux={{tmuxSession}}. 한국어로 중단 상태를 전달하세요."},"session-end":{enabled:true,gateway:"local",instruction:"OMX hook=session-end session={{sessionId}} tmux={{tmuxSession}} reason={{reason}}. 한국어로 완료 요약을 #omc-dev에 남기세요."}}}' "$CONFIG_FILE" >"$CONFIG_FILE.tmp" && mv "$CONFIG_FILE.tmp" "$CONFIG_FILE"
```

### Compatibility + precedence contract

Explicit `notifications.openclaw` wins when valid; generic aliases are ignored with a warning.

Verify this mode and inspect evidence without hiding output:

```bash
clawdbot agent --session-id omx-hooks --message "OMX hook test via clawdbot agent path" --thinking minimal --deliver --reply-channel discord --reply-to 'channel:CHANNEL_ID' --timeout 120 --json
tmux list-sessions -F '#{session_name}' | rg '^omx-' || true
jq '.notifications.openclaw.hooks' "$CONFIG_FILE"
tail -n 120 /tmp/omx-openclaw-agent.jsonl | jq -s '.[] | {timestamp:(.timestamp // .time),status:(.status // .error // "ok")}'
rg '"error"|"failed"|"timeout"' /tmp/omx-openclaw-agent.jsonl | tail -20
```

For HTTP integrations, run both a `/hooks/wake` smoke test and `/hooks/agent` delivery verification.

## 5. Cross-cutting settings and disable

- Verbosity: `minimal`, `session` (recommended), `agent`, or `verbose`.
- Idle cooldown: `notifications.idleCooldownSeconds` (environment override: `OMX_IDLE_COOLDOWN_SECONDS`).
- Profiles: `notifications.profiles` and `notifications.defaultProfile`.
- Reply listener: `notifications.reply.enabled`; gates are `OMX_REPLY_ENABLED=true` and, for Discord, `OMX_REPLY_DISCORD_USER_IDS=...`. An authorized Discord bot operator may exact-match `status` on a tracked notification for a bounded read-only session summary.

Disable all notifications:

```bash
jq '.notifications.enabled=false' "$CONFIG_FILE" >"$CONFIG_FILE.tmp" && mv "$CONFIG_FILE.tmp" "$CONFIG_FILE"
```

## Exit evidence

Run `jq empty "$CONFIG_FILE"` after edits. Report the config path, enabled native providers, enabled aliases, whether valid `notifications.openclaw` overrides aliases, verbosity, idle cooldown, reply-listener state, and smoke-test results. Stop when the JSON parses and the selected path has its required evidence; do not claim delivery from configuration alone.
