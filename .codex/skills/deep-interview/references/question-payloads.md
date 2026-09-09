# question payloads

Canonical bounded single-choice payload:

```json
{
  "question": "Which execution lane should own this once the interview is complete?",
  "type": "single-answerable",
  "options": [
    {
      "label": "Plan first",
      "value": "ralplan",
      "description": "Need architecture and test-shape review before execution"
    },
    {
      "label": "Execute directly",
      "value": "autopilot",
      "description": "Requirements are already explicit enough for planning plus execution"
    },
    {
      "label": "Refine further",
      "value": "refine",
      "description": "Clarification is still needed before any handoff"
    }
  ],
  "allow_other": false,
  "other_label": "Other",
  "source": "deep-interview"
}
```

Canonical bounded multi-select payload:

```json
{
  "question": "Which non-goals must stay out of scope for the first pass?",
  "type": "multi-answerable",
  "options": [
    {
      "label": "No UI redesign",
      "value": "no-ui-redesign",
      "description": "Keep layout and styling unchanged"
    },
    {
      "label": "No new dependencies",
      "value": "no-new-dependencies",
      "description": "Work within the existing toolchain"
    },
    {
      "label": "No API contract changes",
      "value": "no-api-contract-changes",
      "description": "Preserve external request and response shapes"
    }
  ],
  "allow_other": false,
  "other_label": "Other",
  "source": "deep-interview"
}
```

Canonical answer-shape reminders:

```json
{
  "answer": {
    "kind": "option",
    "value": "ralplan",
    "selected_labels": ["Plan first"],
    "selected_values": ["ralplan"]
  }
}
```

```json
{
  "answer": {
    "kind": "multi",
    "value": ["no-new-dependencies", "no-api-contract-changes"],
    "selected_labels": ["No new dependencies", "No API contract changes"],
    "selected_values": ["no-new-dependencies", "no-api-contract-changes"]
  }
}
```
