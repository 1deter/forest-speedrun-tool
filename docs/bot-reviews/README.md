# Bot reviews

One report per review, `<YYYY-MM-DD>.md`, written by the skill
`bot-review` (`.claude/skills/bot-review/SKILL.md`, T-0141): the full eval's
score per question against the last review, the 👎 / partial queue items
handled, what runners said in the knowledge-testing channel, how the
research tasks moved, the tasks filed.

`mark.json` is what the next session-start counts from:
`{"date": "...", "queue_id": <highest queue id seen>, "message_id": "<last knowledge-testing message id read>"}`.
Without it the report says the first review is due. Only the skill moves it.
