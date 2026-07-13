# FlowDictate — AI Dictation for Windows

*Personal project · Product & Engineering · C#/.NET, whisper.cpp, Claude API*

---

## Overview

FlowDictate turns speech into polished writing anywhere in Windows: hold a key, talk naturally — fillers, backtracking and all — release, and clean punctuated text appears at your cursor in about two seconds. Speech recognition runs entirely on-device; a fast LLM pass does what transcription can't: it turns *spoken thought* into *written text* ("let's meet Tuesday — wait, no, Friday" becomes "Let's meet on Friday").

![Demo GIF placeholder](media/demo.gif)
*<!-- 10-second demo: dictating into Notepad, then a voice command on selected text -->*

## My role

Solo — product definition, architecture, implementation, and testing. I scoped the MVP against one measurable promise (release-to-insert in ~2 seconds), built the pipeline, and iterated through real-use failures: a native audio crash, an LLM that answered instead of obeying, clipped first words, and a keyboard default that didn't exist on the actual hardware.

## Technologies

C# / .NET 8 · Win32 low-level keyboard hooks & SendInput · Windows UI Automation · NAudio · Whisper.net (whisper.cpp, on-device) · Anthropic Claude API · WinForms/WPF interop

## Screenshots

| | | |
|---|---|---|
| ![Listening pill](media/pill-listening.png) | ![Command on selection](media/command-demo.png) | ![Tray + settings](media/settings.png) |
| *Non-intrusive status pill* | *"Make this more concise" on a selection* | *Tray menu & JSON settings* |

*<!-- placeholders — capture from a live session -->*

## Key product decisions

- **Privacy as architecture:** the pipeline splits exactly where data stops being biometric — audio is processed locally; only derived text reaches an API, and even that has an offline fallback.
- **A latency budget as the north star:** every model and design choice was tested against "~2 seconds from key release to inserted text." The cleanup model default is the fast one, not the smartest one.
- **Zero new UI to learn:** activation is a key you already have (hold CapsLock; double-tap for hands-free), editing reuses the same gesture on a selection, and the only visual is a focus-proof status pill.
- **Fail to nothing, not to garbage:** stacked gates keep hallucinated text (Whisper inventing "[MUSIC]" from room noise) from ever reaching a document; API failures degrade to offline cleanup rather than error dialogs.

## Business impact

Built as a personal daily-driver and portfolio piece, so impact is measured honestly at that scale:

- **Cost structure:** on-device recognition makes marginal cost ≈ $0.002 per dictation (cleanup tokens only) — an unlimited-use tool for under ~$2/month of realistic usage, vs. $15/month subscriptions in the category.
- **Performance:** ~1.7 s release-to-insert for short phrases (measured), inside the 2-second target that separates "feels like typing" from "feels like a tool."
- **Category insight:** demonstrates a differentiated wedge against cloud-first incumbents — the privacy-split architecture is the positioning, not a feature.

## What I learned

- **Defaults are product decisions** — my first hotkey didn't exist on the target laptop's keyboard. Ship defaults that survive hardware you didn't test.
- **LLM boundaries are product surfaces** — untagged input let a user's sentence be interpreted as a request *to* the model, which typed its chatbot reply into their document. Content/instruction separation is a requirement, not a refinement.
- **Measure at the layer that can lie** — a day was lost to a mic that every software layer reported as healthy while it delivered pure silence (hardware mute). Instrumentation that can't see the failing layer is worse than none.
- **Trust is built in failure paths** — the features users never see (silence gates, offline fallback, clipboard restoration, a persistent log) are what make them keep a tool installed.

## Links

- **Code & docs:** [GitHub repository](#) *<!-- add URL after pushing -->*
- **Deep dives:** [Architecture](architecture.md) · [Case study](case-study.md) · [Product decisions](product-decisions.md)
