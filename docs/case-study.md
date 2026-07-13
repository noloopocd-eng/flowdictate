# Case Study: Building FlowDictate — an AI dictation tool that respects the 2-second rule

*A product case study on building a Wispr Flow-style dictation app for Windows: on-device speech recognition, an LLM cleanup pass, and the product decisions in between.*

---

## The problem

I think at speaking speed and write at typing speed. Most people do: natural speech runs 150+ words per minute; typing runs around 45. That gap is why voice memos pile up untranscribed and why long emails get shorter and worse at the end of the day.

Dictation should close the gap, but raw speech-to-text output isn't *writing*. Say this out loud naturally:

> "um, let's meet on Tuesday — wait, no, Friday — at 3pm to discuss the, you know, quarterly report"

A literal transcriber types exactly that. Now you're editing instead of typing, and the time you saved is gone. The product insight: **the value isn't transcription, it's the transformation from spoken thought to written text.**

## Why existing solutions weren't enough

- **Windows built-in dictation (Win+H):** literal transcription — no filler removal, no self-correction handling, punctuation only by voice command ("comma"). The editing tax eats the benefit.
- **Wispr Flow** (the product that inspired this): excellent UX, but transcription happens in the cloud — your raw voice audio leaves the machine — and it's $15/month. I wanted the privacy boundary drawn differently.
- **Superwhisper-style local tools:** private, but most stop at transcription; the polish step — the actual product — is missing or weak.

Nothing on Windows offered: *local* speech recognition, *state-of-the-art* text cleanup, works in *every* app, at effectively zero marginal cost.

## My goals

1. **The 2-second rule:** hotkey release → clean text at cursor in ~2 seconds. Beyond that, dictation feels like a tool; under it, it feels like typing with your voice.
2. **Privacy as an architecture, not a policy:** raw audio must never leave the device. Only derived text may.
3. **Works everywhere:** Word, Outlook, Slack, VS Code, any browser field — no per-app integrations.
4. **Fail soft:** a failed dictation may cost a shrug, never a crash, never garbage typed into a document.

## User journey

1. Priya is writing a project update in Outlook. She clicks where the next paragraph goes.
2. She **holds CapsLock** and talks the way she thinks — fillers, backtracking, and all. A small pill at the bottom of the screen pulses red: it's listening.
3. She releases. The pill turns blue (processing), then green (inserted). Her spoken ramble is now two clean, punctuated sentences — in Outlook's formal register, because the tool knows where it's typing.
4. Re-reading, she finds a sentence she doesn't like. She selects it, holds CapsLock, and says *"make this more concise."* The selection is rewritten in place.
5. She never opened another window. The tool has no UI in her way — just a key she already had.

## Product decisions

**The privacy split.** The pipeline is split at exactly the point where data becomes non-biometric: speech-to-text happens on-device (whisper.cpp); only the resulting *text* goes to an API for cleanup. Users who want zero cloud get an offline cleanup fallback — and the product still works, just with lighter polish.

**Hold-to-talk on a key that already exists.** No app to open, no window to focus. CapsLock was chosen after the first choice (Right Ctrl) failed a real-world test: many laptops don't have one. CapsLock exists on every keyboard, is rarely used intentionally, and its normal function is preserved behind Shift+CapsLock. Hold = push-to-talk; double-tap = hands-free. The lesson: defaults are product decisions, and they fail on hardware you didn't test.

**Latency budget over model quality.** Cleanup uses a fast model (~0.9 s) rather than the highest-quality one (~3 s), because the 2-second rule beats marginal cleanup quality — a dictation tool that feels slow doesn't get used. Model choice is a setting, so power users can flip the trade.

**One interaction, two intents.** With text selected, speech could be dictation *or* an instruction about the selection. Instead of a second hotkey (more UI to learn), the LLM decides: "make this shorter" transforms the selection; ordinary speech replaces it, matching what typing over a selection does. Ambiguity resolved by intelligence, not interface.

**Insert nothing over inserting garbage.** Whisper hallucinates plausible text from ambient noise ("[MUSIC]", or eerily, full sentences). Three stacked gates (audio energy, the model's own no-speech probability, artifact patterns) mean a silent hold types nothing at all.

## Technical architecture

C#/.NET 8 tray application. Global hotkey via a low-level keyboard hook driving a small state machine (hold/double-tap/cancel). Continuously-hot microphone stream with a 1-second ring buffer, so each dictation includes 0.4 s of *pre-roll* — first words are never clipped by device startup. On-device transcription through Whisper.net (whisper.cpp). Cleanup through the Anthropic C# SDK behind an `ITextCleaner` interface with an offline fallback. Insertion via Windows UI Automation where possible, clipboard-swap paste where not. Every stage behind an interface; every stage timed and logged.

*(Full details: [architecture.md](architecture.md))*

## Challenges

- **A native crash on key release.** Disposing the audio device while its capture thread was mid-callback corrupted memory and killed the process — my automated tests passed by timing luck; the first real user keystroke crashed. Fix: dispose only after the device confirms its thread stopped. Lesson: lifecycle races around native resources don't show up until real-world timing finds them.
- **The AI answered instead of obeying.** A user said "turn this into bullet points" with no readable selection — and the cleanup model *replied like a chatbot*, typing "Could you please provide the transcript?" into their document. Fix: wrap the transcript in explicit tags with a hard rule that its content is never addressed to the model. Lesson: in LLM products, the boundary between *content* and *instruction* is a security surface — even in a dictation app.
- **Debugging the physical world.** The mic delivered perfect silence for a day — every software layer reported healthy. The cause lived below the OS (a hardware mute + audio-stack wedge). Lesson: when telemetry says "fine" and reality says "broken," your instrumentation is measuring the wrong layer; I also learned my mic-level monitor read zero unless a capture stream was open — the diagnostic itself was wrong.
- **Electron hides everything.** Selection reading via UI Automation returns nothing in Electron apps (Slack, VS Code, most chat apps). Fix: fall back to sampling the selection with a synthesized Ctrl+C and restoring the clipboard — with a terminal blacklist, because Ctrl+C in a terminal kills processes.

## Lessons learned

1. **Latency is a feature with a budget.** Every stage was measured from day one; every model/quality decision was made against the 2-second rule, not in the abstract.
2. **Test on hardware you didn't design for.** Right Ctrl didn't exist on the actual user's laptop. The best default is the one that survives contact with real machines.
3. **LLM output is untrusted input to your own product.** Prompt hardening, output gating, and "insert nothing" paths are product requirements, not nice-to-haves.
4. **Graceful degradation earns trust.** Expired API key? Offline? The tool quietly steps down to rule-based cleanup instead of failing. Users forgive lower quality; they don't forgive breakage.
5. **A persistent log is the cheapest feature you'll ever ship.** Two crashes and one "it's not working" were each diagnosed in minutes from a timestamped pipeline log.

## Future roadmap

- **Streaming transcription** while the user speaks — the single biggest latency win available (transcript is ready at key release; only cleanup remains).
- **Voice snippets** ("insert my signature") and a **settings UI** to replace the JSON file.
- **GPU inference** for larger local models at interactive speed.
- **A signed installer + auto-update** — the step from "my tool" to "a product."
- **Longitudinal quality metrics:** track edit-after-insert rate — the truest measure of whether the cleanup is doing its job.
