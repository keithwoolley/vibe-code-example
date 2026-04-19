# Medication Tracker — Specification

A browser-local, single-user medication tracker. When an alarm time arrives and the app is open
in a browser tab, a sound plays and a modal prompts **Take** or **Ignore**. The response *is*
the adherence log — there is no separate "log a dose" action. The app shows today's timeline, a
persistent "missed today" list, and a rolling 30-day history. No account, server, or network.

The app SHOULD be usable on desktop (≥ 1024px) and mobile (≥ 360px) browsers.

---

## Table of Contents

1. [Goals](#1-goals)
2. [Design Principles](#2-design-principles)
3. [Domain Model](#3-domain-model)
4. [Time and the Day Boundary](#4-time-and-the-day-boundary)
5. [Scheduling](#5-scheduling)
6. [Alarm Flow](#6-alarm-flow)
7. [State Machine (summary)](#7-state-machine-summary)
8. [Screens](#8-screens)
9. [Persistence](#9-persistence)
10. [Out of Scope](#10-out-of-scope)
11. [Acceptance Scenarios](#11-acceptance-scenarios)

---

## 1. Goals

- **G1.** Play an audible alarm at each user-configured time for each medication, while the app
  is open in a browser tab.
- **G2.** Make the alarm response the single, unambiguous way to record whether a dose was taken.
- **G3.** Show today's dose schedule with a prominent list of today's missed doses.
- **G4.** Retain 30 days of dose history for review.
- **G5.** Run entirely in the browser, offline after initial load.

## 2. Design Principles

1. **Alarm-first.** The alarm and its two-button response are the core interaction. Every other
   screen supports that primitive.
2. **Local and offline by default.** All state lives in the browser; no network at runtime.
3. **Minimal mandatory data.** Only the medication name is required.
4. **Immutability of the past.** Today's state is freely editable; previous days freeze at
   midnight. This keeps the history trustworthy and the UI simple.
5. **Prescriptive defaults over configuration.** One built-in sound, fixed volume, fixed
   midnight day boundary. No settings surface beyond medications themselves.

## 3. Domain Model

### 3.1 Medication

```
Medication {
  id:         string
  name:       string          -- required, non-empty after trim
  doseAmount: string | null   -- free-text, e.g., "10", "1/2"
  doseUnit:   string | null   -- free-text, e.g., "mg", "tablet"
  form:       Form  | null    -- enum (§3.1.1)
  notes:      string | null
  isPRN:      boolean         -- true = as-needed, no alarms
  alarmTimes: string[]        -- "HH:MM" strings; empty iff isPRN
}
```

#### 3.1.1 `Form` enum

One of: `pill`, `capsule`, `tablet`, `liquid`, `injection`, `inhaler`, `patch`, `drop`, `other`.
Rendered as a dropdown with a blank (unset) option.

#### 3.1.2 Validation

- `name` MUST be non-empty after trimming.
- If `isPRN = false`, `alarmTimes` MUST contain at least one entry; duplicates rejected.
- If `isPRN = true`, `alarmTimes` MUST be empty.

### 3.2 DoseEvent

```
DoseEvent {
  id:            string
  medicationId:  string
  date:          "YYYY-MM-DD"    -- local calendar date
  scheduledTime: string | null   -- "HH:MM"; null for PRN events
  status:        pending | due | taken | ignored
}
```

| Status | Meaning |
|---|---|
| `pending` | Scheduled for today but not yet due. |
| `due` | The alarm is currently ringing for this event. At most one at a time. |
| `taken` | User pressed **Take** (scheduled) or logged via **Took it now** (PRN). |
| `ignored` | User pressed **Ignore**. The dose is considered missed for the day. |

Finalized days (any day before today) MUST NOT contain `pending` or `due` events — rollover
transitions any such events to `ignored` (§4).

## 4. Time and the Day Boundary

All times and dates use the browser's local time zone. "Today" is the current local calendar
day (00:00 inclusive to 24:00 exclusive). No DST or time-zone reconciliation beyond what the
platform clock provides.

**Rollover.** At midnight, and on every app open, the app MUST:

1. Mark every pre-today `pending`/`due` event as `ignored`.
2. For each non-PRN medication × alarm time, create today's `pending` `DoseEvent` if it does
   not already exist.
3. Delete `DoseEvent`s older than 30 days (today + 29 prior).
4. Re-render the Today screen.

Medications themselves are never auto-deleted — only historical dose events.

## 5. Scheduling

**Daily-only.** Every alarm time on every medication means "fire every day at this local time".
There is no weekday filter, no every-N-days interval, no start/end date.

**Past-time alarms on app open.** If the app opens at 10:00 with an 08:00 alarm not yet logged
for today, the app MUST materialize the event as `pending` and then immediately treat it as
due (firing the alarm flow in §6). If multiple past-time alarms were missed, they queue per §6.

**PRN logging.** A PRN medication fires no alarms. The Today screen offers a **Took it now**
affordance that creates a `DoseEvent` with `scheduledTime = null`, `status = taken`,
`date = today`.

## 6. Alarm Flow

An alarm fires when a `DoseEvent` transitions `pending` → `due` — either because the local
clock reached its `scheduledTime`, or on app open for a past-time pending event. The app MUST:

1. Set `status = due`.
2. Loop the built-in alarm sound.
3. Show a modal naming the medication, also showing dose, form, and notes if set, with exactly
   two buttons: **Take** and **Ignore**.

The modal MUST be truly modal: no dismiss-by-click-outside, no escape-key dismiss. It remains
on screen until Take or Ignore is pressed.

- **Take** → `status = taken`; sound stops; modal closes.
- **Ignore** → `status = ignored`; sound stops; modal closes. The event will NOT re-ring today.
  Tomorrow's rollover creates a fresh `pending` event, so the alarm WILL ring again on
  subsequent days.

**One alarm at a time.** Only one alarm modal is ever open. If multiple events become `due` at
once (shared alarm time, or multiple past-time alarms on open), process them sequentially in
any deterministic order — e.g., ascending `scheduledTime`.

**Sound.** One bundled audio file, suitable for seamless looping, played at its authored level
(subject to the OS/browser volume). No volume control. The sound loops until Take or Ignore.

**Tab-open-only.** Alarms only fire while the app is open in a browser tab. If the tab is
closed at the scheduled time, the alarm does not fire; the next app open will apply the
past-time handling above.

## 7. State Machine (summary)

A `DoseEvent` is created `pending` by rollover or on-open materialization. It becomes `due`
when its scheduled time is reached. From `due` it resolves to `taken` (Take) or `ignored`
(Ignore).

On the **Today** screen, the user MAY freely flip any event between `taken` and `ignored` by
tapping the row. Flips to or from `pending` / `due` via manual edit are NOT allowed — those
states are system-managed.

On any day before today, all events are strictly read-only.

## 8. Screens

Single-page app with three primary screens plus the alarm modal. A persistent nav provides
access to **Today**, **Medications**, **History**. Today is the default landing view.

### 8.1 Today

Two regions:

**Timeline.** Today's `DoseEvent`s in ascending `scheduledTime` order, with PRN events
interleaved in log order. Each row shows medication name, dose (if set), time (or "PRN"),
and current status with a visual indicator. Tapping a `taken` or `ignored` row flips between
the two. `pending` and `due` rows are resolved only via the alarm flow.

**Missed today.** A persistent, always-visible section listing today's `ignored` events,
updated in real time. If empty, show a neutral placeholder ("No missed doses yet") — do not
hide the section.

A **Took it now** affordance opens a picker of PRN medications and creates the PRN event
(§5).

**First run.** If no medications exist, show a prominent call-to-action routing to the
medication form.

### 8.2 Medications

List of every medication: name, dose (if set), form (if set), schedule summary
("08:00, 20:00" or "PRN"). **Add medication** opens the editor. Tapping a row opens the
editor for that medication.

Editor fields:

| Field | Input |
|---|---|
| Name | Single-line text, required |
| Dose amount | Single-line text, optional |
| Dose unit | Single-line text, optional |
| Form | Dropdown (§3.1.1) or blank |
| Notes | Multi-line text, optional |
| As-needed (PRN) | Checkbox. When checked, alarm-times editor is hidden. |
| Alarm times | HH:MM inputs with add/remove. Hidden when PRN is checked. At least one entry required when PRN is unchecked. |

**Delete** (in edit mode, after confirmation) removes the medication AND every `DoseEvent`
referencing it — today's timeline and all 30 days of history. The confirmation dialog MUST
say so ("This will also delete this medication's history.").

### 8.3 History

Last 30 days, selectable via date picker or date list. Selected day shows the same row shape
as Today but strictly read-only. A per-day summary ("5 taken · 1 missed") is optional.

## 9. Persistence

All state lives in browser-local storage. IndexedDB is preferred given the multi-entity model;
`localStorage` with a single JSON blob is acceptable. The choice is invisible to the user.

The app MUST NOT make network requests after initial page/asset load — no analytics, no
telemetry, no runtime CDN fetches. The alarm sound asset MUST be bundled.

Clearing browser data or switching browsers/devices presents a fresh, empty app. No runtime
warning needed.

## 10. Out of Scope

Implementers MUST NOT speculatively add: push / background notifications when the tab is
closed; multi-device sync or accounts; non-daily schedules (weekdays, every-N-days, courses,
start/end dates); snooze or auto-ignore after timeout.

## 11. Acceptance Scenarios

Each MUST pass against the final implementation.

### 11.1 First-run add and schedule
**Given** no medications. **When** the user saves a medication "Lisinopril" with dose "10 mg",
form "tablet", alarm 08:00. **Then** the Medications list shows it with schedule "08:00", and
Today shows one row for Lisinopril at 08:00 with an appropriate status.

### 11.2 Alarm rings → Take
**Given** a `pending` event at 08:00. **When** the local clock reaches 08:00 with the tab open,
**then** the sound loops and the modal opens. **When** the user presses **Take**, **then** the
sound stops, the modal closes, and status = `taken`.

### 11.3 Alarm rings → Ignore
Same setup. **When** the user presses **Ignore**, **then** sound stops, modal closes, status =
`ignored`, the event appears in "Missed today", and it does not re-ring today.

### 11.4 Ignored dose re-rings the next day
**Given** an `ignored` event on day D-1. **When** midnight passes (or the app is opened on day
D after being closed). **Then** rollover has run, a fresh `pending` event exists for today at
the scheduled time, and the alarm will fire at that time.

### 11.5 Edit today's log
**Given** a `taken` event today. **When** the user taps it. **Then** it flips to `ignored` and
"Missed today" updates in real time. The reverse flip works identically.

### 11.6 Finalized day is read-only
**Given** a DoseEvent dated earlier than today. **When** viewed on History. **Then** no
control allows editing it.

### 11.7 Past-time alarm on app open
**Given** a scheduled medication with alarm 08:00 and no DoseEvent for today. **When** the
user opens the app at 10:00. **Then** a `pending` event materializes for 08:00 and the alarm
flow fires immediately.

### 11.8 PRN manual log
**Given** a PRN medication "Ibuprofen". **When** the user presses **Took it now** and picks
Ibuprofen. **Then** a new `DoseEvent` is created with `status = taken`, `scheduledTime = null`,
and it appears in today's timeline.

### 11.9 Delete medication clears history
**Given** a medication with events today and on prior days. **When** the user deletes it after
confirming. **Then** the medication is gone from the Medications list, from Today, and from
every History day, and no `DoseEvent` with its `medicationId` remains in storage.
