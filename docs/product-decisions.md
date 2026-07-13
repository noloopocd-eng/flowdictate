# FlowDictate — Product Decisions, Feature by Feature

A PM-style breakdown of every shipped feature: the user problem, why it exists, alternatives considered, trade-offs accepted, and how success is measured.

---

## 1. Global hotkey (hold CapsLock / double-tap for hands-free)

- **User problem:** Dictation tools that live in their own window break flow — you leave the document to use them, then paste back.
- **Why it exists:** Zero-UI activation is the core promise. The user's hands are already on the keyboard; one held key is the cheapest possible gesture.
- **Alternatives considered:** A dedicated app window (breaks flow); Win+H-style invocation (conflicts with the OS); Right Ctrl (first choice — failed because many laptops physically lack it); a multi-key chord (harder to hold while speaking).
- **Trade-offs:** CapsLock loses its native function (mitigated: Shift+CapsLock still toggles caps; key is configurable); a low-level keyboard hook requires very careful threading (blocking it lags the whole system's keyboard).
- **Success metrics:** Activation success rate (hotkey press → session started), accidental-activation rate (stray-tap cancels ÷ sessions), % of users who rebind the key.

## 2. On-device transcription (whisper.cpp)

- **User problem:** People won't dictate sensitive content (emails, medical notes, legal text) into a tool that ships raw audio to a server.
- **Why it exists:** Privacy as architecture: the biometric artifact (voice) never leaves the device — and marginal cost per dictation drops to ~zero, enabling unlimited use.
- **Alternatives considered:** Cloud STT (better accuracy, but breaks the privacy promise and adds cost + network latency); Windows built-in recognition (weak accuracy); larger local models (better accuracy, too slow on CPU).
- **Trade-offs:** CPU inference is slower than cloud (2 s for a 5 s utterance on small.en) and occasionally mishears words a cloud model wouldn't; model files are a 142–466 MB download.
- **Success metrics:** Word error rate on user vocabulary, transcription latency P50/P95, edit-after-insert rate.

## 3. AI cleanup pass (Claude)

- **User problem:** Literal transcripts aren't writing — fillers, false starts, no punctuation, spoken self-corrections left in ("Tuesday, wait no, Friday").
- **Why it exists:** This is the actual product. Removing the editing tax is what makes dictation faster than typing in practice, not just in wpm arithmetic.
- **Alternatives considered:** Rules/regex only (ships as the offline fallback — handles fillers and punctuation but cannot resolve self-corrections, which need language understanding); a local LLM (privacy-perfect but too slow on CPU for the 2-second rule); fine-tuned small model (maintenance burden not justified for a personal tool).
- **Trade-offs:** Requires an API key and pennies of cost; text (not audio) goes to the cloud — an explicit, documented boundary; adds ~0.9 s of latency. Model is user-swappable (fast Haiku default vs. higher-polish Opus at ~3× latency).
- **Success metrics:** Cleanup latency P50/P95, self-correction resolution accuracy, edit-after-insert rate, fallback activation rate.

## 4. Universal text insertion (UIA → clipboard paste)

- **User problem:** A dictation tool that only works in its own textbox forces copy-paste — flow broken.
- **Why it exists:** "Works everywhere" is a table-stakes promise of the category; per-app integrations don't scale to one developer.
- **Alternatives considered:** Simulated per-character keystrokes (slow, breaks with IMEs and keyboard layouts); UIA only (fails in Electron/rich editors); clipboard only (works broadly but touches user state — clipboard must be preserved).
- **Trade-offs:** Clipboard path briefly replaces then restores clipboard *text* (images aren't preserved — documented); UIA path can reposition the caret in some controls.
- **Success metrics:** Insertion success rate by app; strategy mix (UIA vs. paste); clipboard-restore failure reports.

## 5. Voice commands on selected text

- **User problem:** Dictated (or typed) text often needs one more pass: shorter, more formal, bulleted. Leaving to an AI chat window costs the flow the tool exists to protect.
- **Why it exists:** It reuses the exact interaction users already learned (select → hold key → speak) to unlock editing, not just creation.
- **Alternatives considered:** A second "command" hotkey (Wispr's approach — clearer, but doubles the interface to learn); trigger words ("command: ..."); no disambiguation (treat all speech-with-selection as instructions — breaks dictate-over-selection).
- **Trade-offs:** Intent ambiguity is delegated to the LLM (instruction vs. dictation) — occasionally it will guess wrong; selection reading needs a clipboard fallback in Electron apps, which briefly borrows the clipboard.
- **Success metrics:** Command usage per active day, intent-classification error rate, undo-after-command rate.

## 6. App-aware tone

- **User problem:** The same sentence should land differently in Outlook and in Slack; users otherwise re-edit for register.
- **Why it exists:** The foreground app is free context — one process-name lookup buys meaningfully better default output.
- **Alternatives considered:** Manual tone toggle (more UI, more friction); no tone handling (fine, but leaves easy quality on the table); full app-content awareness (privacy-invasive, out of scope).
- **Trade-offs:** Process-name mapping is coarse (a browser is "one app" regardless of site); the map is a config file, not learned.
- **Success metrics:** Edit-after-insert rate split by app category; user modifications to the tone map.

## 7. Custom dictionary

- **User problem:** Recognizers reliably butcher names, brands, and jargon — the words users care most about getting right.
- **Why it exists:** A ten-word list fixes the most visible quality failures; it hints the recognizer *and* instructs the cleanup model on authoritative spellings.
- **Alternatives considered:** Automatic learning from user corrections (the right long-term answer; needs correction telemetry that doesn't exist yet); per-app dictionaries (over-engineering at this stage).
- **Trade-offs:** Manual curation; very long lists dilute the recognizer hint (Whisper's prompt window is limited).
- **Success metrics:** Recognition accuracy on dictionary terms before/after; dictionary adoption rate.

## 8. Status pill (floating indicator)

- **User problem:** With an invisible tool, "is it listening?" becomes anxiety — users repeat themselves or trail off.
- **Why it exists:** Trust needs a heartbeat: red pulsing = listening, blue = processing, green = inserted. It answers the only three questions the user has.
- **Alternatives considered:** Tray-icon color only (invisible at taskbar distance); sound cues (annoying in meetings/offices); full window (steals focus — fatal, since insertion targets the focused app).
- **Trade-offs:** Screen real estate at bottom-center; had to be built as a non-activating, click-through window (`WS_EX_NOACTIVATE`) so it can never intercept focus.
- **Success metrics:** Repeat-dictation rate (proxy for "wasn't sure it heard me"), support of "did it hear me?" confusion.

## 9. Hot mic with pre-roll

- **User problem:** First words were clipped ("turn this into..." arrived as "this into...") — users start speaking the instant they press.
- **Why it exists:** Opening an audio device takes real time. Keeping the stream hot with a 1 s ring buffer and seeding each session with 0.4 s of pre-roll makes the tool tolerant of natural timing.
- **Alternatives considered:** Telling users to pause before speaking (fighting human nature); faster device open (physics says no); pushing the whole session later (adds latency).
- **Trade-offs:** The OS mic-in-use indicator shows while the app runs (honest, but users may ask); a rolling 1 s of ambient audio exists in memory — never processed, never persisted, and documented.
- **Success metrics:** First-word truncation rate; privacy-question frequency.

## 10. Graceful degradation & failure gates

- **User problem:** Tools that crash, or worse, type garbage into your document, get uninstalled.
- **Why it exists:** Every failure has a designed floor: API down → offline cleaner; no speech → insert nothing (three stacked gates against Whisper hallucinations); UIA refused → paste; anything unexpected → log and end quietly.
- **Alternatives considered:** Error dialogs (interrupting the user mid-flow to report a failure they can't act on); retry loops (latency).
- **Trade-offs:** Silent degradation can mask problems — mitigated by a persistent timestamped log of every stage.
- **Success metrics:** Crash-free sessions, garbage-insertion reports (target: zero), fallback activation rates as an early-warning dashboard.
