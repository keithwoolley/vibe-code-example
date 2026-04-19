# Medication Tracker — Natural Language Specification

This document is a language-agnostic specification for a browser-based medication tracker. It
describes the behavior, data model, user interface, and operational rules of the application in
prose precise enough to implement and validate against. It is intended to be consumed directly
by a coding agent (e.g., Claude Code, Codex) or a human developer.

The specification is patterned after the StrongDM attractor NLSpec style: hierarchical sections,
prescriptive prose (`MUST`, `SHOULD`, `MAY`), reference tables, pseudocode where useful, and a
Definition of Done that treats the spec itself as testable.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Design Principles](#3-design-principles)
4. [Glossary](#4-glossary)
5. [Domain Model](#5-domain-model)
6. [Time and the Day Boundary](#6-time-and-the-day-boundary)
7. [Scheduling and the Daily Plan](#7-scheduling-and-the-daily-plan)
8. [Alarm Behavior](#8-alarm-behavior)
9. [Dose Event State Machine](#9-dose-event-state-machine)
10. [Screens and User Flows](#10-screens-and-user-flows)
11. [Persistence](#11-persistence)
12. [Non-Functional Requirements](#12-non-functional-requirements)
13. [Out of Scope](#13-out-of-scope)
14. [Acceptance Scenarios](#14-acceptance-scenarios)
15. [Definition of Done](#15-definition-of-done)
16. [Appendix A — Field Reference](#appendix-a--field-reference)
17. [Appendix B — Event Taxonomy](#appendix-b--event-taxonomy)

---

## 1. Overview

The application is a single-user, browser-local web app that reminds its user to take their
medications on a daily schedule. When an alarm time arrives and the app is open in a browser
tab, a sound plays and a modal dialog prompts the user to either **Take** (confirming the dose
was taken) or **Ignore** (confirming the dose will not be taken today). The app records each
dose event and surfaces a persistent list of today's missed doses, plus a rolling 30-day
history. No account, server, or network connection is involved.

The alarm response *is* the adherence log: there are no separate "log a dose" and "dismiss
reminder" actions. Turning off an alarm via **Take** logs the dose as taken; pressing **Ignore**
logs it as missed.

## 2. Goals and Non-Goals

### 2.1 Goals

- **G1.** Play a clearly audible alarm at each user-configured time for each medication, while
  the app is open in a browser tab.
- **G2.** Make the response to the alarm the single, unambiguous way to record whether a dose
  was taken.
- **G3.** Present the user a live view of today's dose schedule, and a prominent running list
  of today's missed doses.
- **G4.** Retain and display the last 30 days of dose history so the user can review past
  adherence.
- **G5.** Run entirely in the user's browser with no backend, account, or network dependency.

### 2.2 Non-Goals

This release does not attempt to solve: multi-device sync, cross-user sharing or caregiver
access, refill / inventory management, pharmacy integration, drug interaction or contraindication
checking, accessibility conformance beyond what HTML defaults provide, localization other than
English, time-zone or DST handling, push / background notifications when the tab is closed,
non-daily schedules (weekly, every-N-days, courses of treatment), custom or user-uploaded alarm
sounds, or export of history to CSV / JSON / PDF. These are listed exhaustively in §13.

## 3. Design Principles

1. **Alarm-first.** The alarm and its two-button response are the core interaction. Every other
   screen supports or extends this primitive.
2. **Local and offline by default.** All state lives in the browser. The app MUST function
   correctly with no network connection at any point after initial page load.
3. **Minimal mandatory data.** Only the medication name is required. Every other field is
   optional so a user can start tracking a new medication in under ten seconds.
4. **Immutability of the past.** Today's state is freely editable; previous days are frozen
   at midnight. This keeps the history trustworthy and the UI simple.
5. **Prescriptive defaults over configuration.** One built-in alarm sound, fixed volume, fixed
   day boundary (midnight local). The app intentionally exposes no settings surface beyond
   medications themselves.
6. **Forgiveness in the moment, accuracy over the long term.** The user can freely flip today's
   dose events between taken and missed until midnight. After midnight, the record stands.

## 4. Glossary

| Term | Definition |
|---|---|
| **Medication** | A user-defined entity representing a drug, supplement, or other substance the user wants to track. Has a name and optional descriptive fields. |
| **Alarm time** | A time-of-day (HH:MM, 24-hour) attached to a medication at which an alarm SHOULD fire each day. |
| **Scheduled medication** | A medication that has one or more alarm times. |
| **PRN medication** | A medication flagged as "as-needed" that has no alarm times and is logged manually via a **Took it now** button. (PRN = Latin *pro re nata*.) |
| **Dose event** | A record that a specific dose was taken, missed (ignored), or is still pending for a specific medication on a specific day. |
| **Alarm** | The runtime event of a scheduled dose becoming due: sound plays and a modal appears, requiring a Take / Ignore response. |
| **Today** | The current calendar day in the browser's local time zone, from 00:00:00 inclusive to 24:00:00 exclusive. |
| **Finalized day** | Any day earlier than today. Its dose events are read-only. |
| **Main screen** / **Today screen** | The default landing view showing today's timeline and the persistent missed-today list. |

## 5. Domain Model

The application's durable state consists of three entity types: `Medication`, `AlarmTime`, and
`DoseEvent`. All three live in browser-local storage (see §11).

### 5.1 Medication

```
Medication {
  id:           string          -- stable unique id, generated on creation
  name:         string          -- required, non-empty, trimmed; up to 120 chars
  doseAmount:   string | null   -- optional, free-text (e.g., "10", "1/2")
  doseUnit:     string | null   -- optional, free-text (e.g., "mg", "tablet", "mL")
  form:         Form | null     -- optional, enum: see §5.1.1
  notes:        string | null   -- optional, free-text, up to 1000 chars
  isPRN:        boolean         -- true = as-needed, no alarms; false = scheduled
  alarmTimes:   AlarmTime[]     -- empty iff isPRN is true
  createdAt:    timestamp
  updatedAt:    timestamp
}
```

#### 5.1.1 `Form` enum

One of: `pill`, `capsule`, `tablet`, `liquid`, `injection`, `inhaler`, `patch`, `drop`, `other`.
The UI SHOULD render a dropdown of these options plus a blank (unset) option.

#### 5.1.2 Validation

- `name` MUST be non-empty after trimming whitespace.
- If `isPRN` is `false`, `alarmTimes` MUST contain at least one entry.
- If `isPRN` is `true`, `alarmTimes` MUST be empty.
- `doseAmount` and `doseUnit` are both optional but SHOULD be captured together when the user
  provides either (the UI MAY leave either blank).

### 5.2 AlarmTime

```
AlarmTime {
  hour:   integer  -- 0..23
  minute: integer  -- 0..59
}
```

Alarm times are stored in the user's local time. There is no seconds precision; alarms fire at
the top of the minute.

A medication's `alarmTimes` array SHOULD be stored in ascending chronological order. Duplicate
alarm times on the same medication MUST be rejected at the form level.

### 5.3 DoseEvent

A `DoseEvent` represents either an expected or recorded dose on a particular day.

```
DoseEvent {
  id:             string       -- stable unique id
  medicationId:   string       -- FK → Medication.id
  date:           Date         -- local calendar date (YYYY-MM-DD)
  scheduledTime: AlarmTime | null
                               -- the time this dose was scheduled for;
                               -- null for PRN doses logged manually
  status:         Status       -- see §5.3.1
  statusChangedAt: timestamp   -- when status last transitioned
  source:         Source       -- see §5.3.2
}
```

#### 5.3.1 `Status` enum

| Status | Meaning |
|---|---|
| `pending` | The dose is scheduled for today but not yet due (`now < scheduledTime`). |
| `due` | The alarm is currently ringing for this dose. At most one `DoseEvent` may be in this state at a time across all medications (see §8.3). |
| `taken` | The user pressed **Take** (scheduled) or logged via **Took it now** (PRN). |
| `ignored` | The user pressed **Ignore**. The dose is considered missed for the day. |

Finalized days (any day before today) MUST NOT contain `pending` or `due` events — any such
events still in those states at midnight are transitioned to `ignored` as part of day rollover
(§6.3).

#### 5.3.2 `Source` enum

| Source | Meaning |
|---|---|
| `scheduled` | Auto-generated from a medication's alarm time for the day. |
| `prn` | Created by the user tapping **Took it now** on a PRN medication. |
| `manual-edit` | Created or transitioned as a result of the user editing today's log directly (e.g., flipping a status from the timeline). |

### 5.4 Day

A `Day` is not a separate stored entity; it is derived by grouping `DoseEvent`s by their `date`
field. The UI computes per-day views on demand.

## 6. Time and the Day Boundary

### 6.1 Local time only

All times and dates are in the browser's local time zone, taken from `Date` at the moment of
use. The app does not attempt to track or reconcile time-zone changes; if the user travels, an
alarm set for 08:00 will fire at 08:00 wherever the browser currently is. No DST adjustments
are made beyond what the platform's built-in clock handles.

### 6.2 Definition of "today"

"Today" is the set of instants `t` where `floor(t, DAY)` equals `floor(now(), DAY)` in the
browser's local time zone. A new day starts at 00:00:00 local time.

### 6.3 Day rollover (midnight)

At 00:00 local, the app MUST perform day rollover. Rollover is triggered by whichever happens
first of:

- A running `setTimeout` scheduled for the next midnight, OR
- The app being opened / reloaded, detecting that the last-observed date is earlier than the
  current date.

Day rollover performs, in order:

1. For every `DoseEvent` whose `date` is earlier than today and whose `status` is `pending` or
   `due`: set `status = ignored`, `statusChangedAt = previous day 23:59:59.999`,
   `source = scheduled`.
2. Materialize today's scheduled `DoseEvent`s: for each non-PRN `Medication` and each of its
   `alarmTimes`, create a `DoseEvent` with `date = today`, `status = pending`,
   `source = scheduled`.
3. Purge `DoseEvent`s whose `date` is older than 30 days before today (see §6.4).
4. Re-render the Today screen.

### 6.4 History retention

The app retains `DoseEvent`s for the last 30 calendar days (today + 29 prior days). Anything
older MUST be deleted at rollover. Medications themselves are never auto-deleted — only their
historical dose events.

## 7. Scheduling and the Daily Plan

### 7.1 Daily-only schedule

Every alarm time on every medication means: *fire every day at this local time*. There is no
weekday filter, no every-N-days interval, no start/end date. Adding a medication on Tuesday
produces the same repeating alarm schedule as adding it on Saturday.

### 7.2 Materialization of the daily plan

When the app is loaded and at day rollover (§6.3), the app MUST ensure that for every
non-PRN medication and every alarm time on that medication, a `DoseEvent` exists for today
with `source = scheduled`. If the app is opened mid-day, any alarm times that are already
in the past SHOULD be materialized as `pending` events with their normal scheduled time, and
the alarm-scheduling logic (§8.1) decides what to do with them.

### 7.3 Alarms in the past at app open

If the app opens at, say, 10:00 and a medication had an alarm scheduled for 08:00 with no
corresponding `DoseEvent`, the app MUST materialize the `DoseEvent` in state `pending` and
then immediately treat it as due (triggering the alarm flow in §8). The user has the choice to
**Take** (recording the dose as taken) or **Ignore**.

Past-time alarms are not stacked: if multiple alarm times were missed while the app was closed,
they are queued per §8.3.

### 7.4 PRN logging

A PRN medication displays a **Took it now** button on the Today screen and on its own
medication detail view. Pressing it MUST:

1. Create a new `DoseEvent` with `medicationId` set, `date = today`,
   `scheduledTime = null`, `status = taken`, `source = prn`, `statusChangedAt = now()`.
2. Append this event to today's timeline in the UI.

PRN medications never fire alarms.

## 8. Alarm Behavior

### 8.1 When an alarm fires

An alarm fires when a `DoseEvent` transitions from `pending` to `due`. This happens when the
current local time reaches or passes the event's `scheduledTime` and no other alarm is
currently ringing. The app MUST:

1. Set the event's `status = due`.
2. Begin looping the built-in alarm sound (§8.5).
3. Display a modal dialog (§8.2) that names the medication and offers **Take** and **Ignore**
   buttons.

### 8.2 Alarm modal

The alarm modal MUST:

- Block interaction with the rest of the Today screen (be a true modal: no dismiss-by-click-
  outside, no escape-key dismiss).
- Display the medication's name prominently.
- Display, if set, the medication's dose (e.g., "10 mg") and form.
- Display the medication's notes if set.
- Offer exactly two action buttons: **Take** and **Ignore**.
- Remain on screen until one of those buttons is pressed.

### 8.3 Sequential queuing

Only one alarm modal is ever open at a time. If two or more medications share an alarm time
(or a past-time alarm fires concurrently with another), the app queues them and processes them
one after the other, in ascending order of `scheduledTime` with medication-name tiebreak
(case-insensitive, alphabetical).

Concretely:

```
FUNCTION tryStartNextAlarm():
    IF an alarm modal is currently displayed:
        RETURN
    candidates := all DoseEvents with status = due (in order above)
    IF candidates is empty:
        RETURN
    event := candidates[0]
    show alarm modal for event
    start looping alarm sound
```

`tryStartNextAlarm` MUST be invoked after every alarm resolution (§8.4), after every transition
from `pending` to `due`, and on app open.

### 8.4 Responding to an alarm

When the user presses a button on the alarm modal:

- **Take**
  1. Set the event's `status = taken` and `statusChangedAt = now()`.
  2. Stop the alarm sound.
  3. Close the modal.
  4. Call `tryStartNextAlarm` (§8.3).

- **Ignore**
  1. Set the event's `status = ignored` and `statusChangedAt = now()`.
  2. Stop the alarm sound.
  3. Close the modal.
  4. Call `tryStartNextAlarm` (§8.3).
  5. The event will **not** be re-raised today. The scheduled time is not re-queued, snoozed,
     or retried before midnight.
  6. Tomorrow's materialization (§6.3 step 2) will produce a fresh `pending` event for this
     medication's alarm time, so the alarm WILL ring again on subsequent days.

### 8.5 Alarm sound

- The app bundles exactly one built-in alarm sound. It MUST be an audio file small enough to
  load quickly and suitable for looping seamlessly.
- The sound loops until the user presses **Take** or **Ignore**.
- The app does not expose a volume control; playback is at the asset's authored level, subject
  to the user's OS / browser volume.
- If the tab is backgrounded (not focused), the sound continues to play but the app does NOT:
  modify the tab's `document.title`, show a browser notification, flash the favicon, or
  otherwise attempt to attract attention beyond the audio.

### 8.6 Tab-open-only delivery

Alarms only fire while the app is open in a browser tab. If the tab is closed, the browser is
closed, or the device is asleep at the scheduled time, the alarm does not fire. When the app
is next opened, any past-time `pending` events for today are materialized and queued as per
§7.3.

## 9. Dose Event State Machine

### 9.1 Transition diagram

```
            materialize (at rollover or open)
     ∅  ───────────────────────────────────→  pending
                                              │
                                              │  time reaches scheduledTime,
                                              │  no other alarm ringing
                                              ▼
                                              due
                                            ╱   ╲
                                    Take   ╱     ╲  Ignore
                                          ▼       ▼
                                        taken   ignored
                                          ▲       ▲
                                          │       │
                        (manual-edit while today is not finalized)
```

### 9.2 Transitions available on today

On the Today screen, for any `DoseEvent` whose `date` is today, the user MAY freely transition
between `taken` and `ignored` by tapping the event in the timeline (§10.2). Each manual flip:

- Updates `status`.
- Sets `statusChangedAt = now()`.
- Sets `source = manual-edit`.

Transitions to or from `pending` / `due` via manual edit are NOT allowed — those states are
system-managed.

### 9.3 Transitions on finalized days

No transitions are permitted on any `DoseEvent` whose `date` is earlier than today. The history
view (§10.5) is strictly read-only.

## 10. Screens and User Flows

The application is a single-page web app with four primary screens:

1. **Today** (default / landing)
2. **Medications list**
3. **Medication detail / editor**
4. **History**

Plus transient overlays: the Alarm modal (§8.2), the Medication form (§10.3), and delete
confirmation dialogs.

### 10.1 Global navigation

A persistent navigation bar or menu MUST provide access to: **Today**, **Medications**,
**History**. The Today screen is the default at app launch.

### 10.2 Today screen

The Today screen is divided into two regions:

**10.2.1 Timeline (primary region).**
A chronological list of today's `DoseEvent`s, ordered by `scheduledTime` ascending (PRN
`taken` events interleaved by `statusChangedAt`). Each row shows:
- Medication name
- Dose (if set), e.g., "10 mg"
- Scheduled time (or "PRN" for PRN events)
- Current status (`pending`, `due`, `taken`, `ignored`)
- A visual status indicator (color or icon per status)

Rows for `taken` and `ignored` events on today are tappable; tapping flips between the two
(per §9.2). Rows for `pending` and `due` events are not directly editable — the only way to
resolve them is via the alarm flow (§8).

The Today screen MUST also contain, at the top or bottom (implementer's choice, but persistently
visible), a **Took it now** affordance that opens a picker of PRN medications; choosing one
creates a PRN `DoseEvent` per §7.4.

**10.2.2 Missed-today section.**
A persistent, always-visible section on the Today screen titled "Missed today" that lists every
`DoseEvent` with `date = today` and `status = ignored`. The list updates in real time as the
user presses **Ignore** or flips events. If empty, the section MAY display a neutral placeholder
("No missed doses yet") but MUST NOT be hidden.

**10.2.3 First-run / empty state.**
If no medications exist, the Today screen MUST show a clear call-to-action to add the first
medication, routing to the Medication form (§10.3).

### 10.3 Medications list

A list of every medication, showing:
- Name
- Dose (if set)
- Form (if set)
- Summary of schedule: a comma-separated list of alarm times (e.g., "08:00, 20:00") or the
  text "PRN" for PRN medications

Affordances:
- **Add medication** button → opens the Medication form.
- Tapping an entry → opens the Medication detail / editor for that medication.

### 10.4 Medication detail / editor

A form for creating or editing a medication. Fields (all manually entered):

| Field | Input |
|---|---|
| Name | Single-line text, required |
| Dose amount | Single-line text, optional |
| Dose unit | Single-line text, optional |
| Form | Dropdown (§5.1.1) or blank, optional |
| Notes | Multi-line text, optional |
| As-needed (PRN) | Checkbox. When checked, the alarm-times editor is hidden / disabled. |
| Alarm times | List of HH:MM inputs with add / remove buttons. Shown only when PRN is unchecked. Must have at least one entry if PRN is unchecked. |

A **Save** button persists the medication. A **Delete** button (in the editor view, not the
create form) deletes the medication after confirmation.

**10.4.1 Deletion semantics.**
Deleting a medication MUST:
- Remove the `Medication` record.
- Remove all `DoseEvent`s for that medication from today's list. Today's timeline updates
  immediately.
- Remove all historical `DoseEvent`s for that medication across the full 30-day retention
  window. History rows for this medication disappear.

The deletion confirmation dialog MUST state this explicitly ("This will also delete this
medication's history.").

### 10.5 History screen

Shows the last 30 days. The primary control is a date picker (or list of dates) limited to the
retained window. Selecting a date displays a per-day list with the same row shape as the Today
timeline (§10.2.1), but every row is read-only.

A summary line per day SHOULD show counts like "5 taken · 1 missed" but this is advisory and
may be rendered minimally.

### 10.6 Alarm modal

Specified in §8.2. The alarm modal is modal over whichever screen the user is currently
viewing and MUST be unblockable by any other app UI.

## 11. Persistence

### 11.1 Storage backend

All persistent state is stored in browser-local storage. The implementer SHOULD use IndexedDB
given the structured, multi-entity model and the size of the 30-day history. `localStorage`
is acceptable as an alternative if the implementation serializes a single compact JSON blob.
The choice MUST NOT be exposed to the user.

### 11.2 Schema

The app MUST store:
- An array of `Medication` records (with embedded `alarmTimes`).
- An array of `DoseEvent` records spanning today and the last 29 days.
- A small metadata record with at minimum:
  - `lastSeenDate`: the local date the app was last open, used to detect if rollover(s) were
    missed and need to be applied (§6.3 fallback path).
  - `schemaVersion`: integer, starting at 1, for forward migration.

### 11.3 No network I/O

The app MUST function end-to-end with no network requests after the initial page + asset load.
No analytics, no telemetry, no CDN lookups at runtime. The alarm sound asset MUST be bundled,
not fetched on demand.

### 11.4 Data loss

Because all data is browser-local and unsynced, clearing browser data, using a different
browser, or using a different device will present a fresh, empty app. The UI SHOULD NOT warn
about this at runtime; the user is assumed to understand browser storage semantics.

## 12. Non-Functional Requirements

### 12.1 Responsiveness

The app MUST be responsive and usable on a typical desktop browser (≥ 1024px wide) and on a
typical mobile browser (≥ 360px wide). Layout SHOULD degrade gracefully; no horizontal
scrolling on supported widths.

### 12.2 Browsers

The app targets the latest two versions of evergreen browsers: Chrome, Edge, Firefox, Safari.
IndexedDB, `setTimeout`, `setInterval`, and HTML5 `<audio>` are assumed available.

### 12.3 Performance

- App cold start (first paint of Today screen) SHOULD complete in under 1 second on a typical
  laptop with cached assets.
- Alarm scheduling MUST be accurate to within ±2 seconds of the scheduled minute boundary
  under normal conditions (tab focused or backgrounded but not throttled beyond platform
  defaults).
- The app MUST not rely on `setInterval` polling at high frequency. A single `setTimeout`
  chained to the next scheduled alarm time is the expected implementation.

### 12.4 Accessibility

Not a priority for this release. Standard HTML form controls and button elements SHOULD be
used so the platform's default accessibility affordances are retained, but no conformance
target (e.g., WCAG AA) is claimed or required.

### 12.5 Security and privacy

- No account, no authentication, no server. There is no passcode / PIN protecting the app —
  anyone with access to the browser can see the data.
- The app MUST NOT transmit user data over the network.

## 13. Out of Scope

The following are explicitly excluded from this release. Implementers MUST NOT add them
speculatively:

- Backend server, user accounts, login, or multi-device sync.
- Caregiver / family / multi-profile usage.
- Push notifications, background alarms when the tab is closed, service workers for alarm
  delivery, installable PWA semantics.
- Non-daily schedules (weekdays only, every-N-days, cycles, courses of treatment with a
  start/end date).
- Snooze, auto-ignore after timeout, or any alarm response other than Take / Ignore.
- Multiple alarm sounds, user-uploaded sounds, volume control, vibration patterns.
- Inventory / pill count tracking, low-stock alerts, refill scheduling.
- Pharmacy data (name, phone, Rx numbers).
- Drug interaction checking, allergy warnings, contraindication alerts.
- Time-zone handling, DST special-casing, anchored time zones.
- Export of history (CSV, JSON, PDF, print).
- Retroactive logging on finalized (past) days.
- Local PIN / passcode / biometric unlock.
- Accessibility conformance to a formal standard.
- Localization to languages other than English.
- Photo of pill / OCR / barcode scan / NDC lookup / prescription import.
- HealthKit, Google Fit, wearables, or any third-party integration.
- Analytics, crash reporting, or any telemetry.

## 14. Acceptance Scenarios

Each scenario is framed as *Given / When / Then* and MUST pass against the final implementation.

### 14.1 First-run add and schedule

- **Given** the app is opened for the first time (no medications, no history).
- **When** the user adds a medication named "Lisinopril" with dose "10 mg", form "tablet",
  and one alarm time at 08:00, and saves.
- **Then** the medication appears in the Medications list with schedule "08:00", and the
  Today screen shows one row for Lisinopril at 08:00 in status `pending` (or `due` / `taken`
  / `ignored` depending on the current time).

### 14.2 Alarm rings and Take is pressed

- **Given** a scheduled medication with an alarm time of 08:00 and a `pending` event for today.
- **When** the local clock reaches 08:00 with the app tab open.
- **Then** the alarm sound loops, the modal opens naming the medication. **When** the user
  presses **Take**, **then** the sound stops, the modal closes, the event's status is `taken`,
  and the Today timeline reflects `taken`.

### 14.3 Alarm rings and Ignore is pressed

- Same setup as §14.2.
- **When** the user presses **Ignore**, **then** the sound stops, the modal closes, the event's
  status is `ignored`, the event appears in the "Missed today" section, and the alarm does not
  re-ring for this dose before midnight.

### 14.4 Ignored dose re-rings the next day

- **Given** a scheduled medication whose alarm time on day D-1 was ignored.
- **When** the local clock passes midnight into day D with the app open (or the app is opened
  on day D after being closed).
- **Then** day D rollover has run, a fresh `pending` event exists for today at the scheduled
  time, and the alarm WILL fire at the scheduled time per §8.1.

### 14.5 Freely edit today's log

- **Given** a dose event for today in status `ignored`.
- **When** the user taps the row in the Today timeline.
- **Then** its status flips to `taken`, `statusChangedAt` updates, the "Missed today" list
  removes this entry, and `source` is set to `manual-edit`.

### 14.6 Finalized day is read-only

- **Given** a dose event whose `date` is yesterday (or any prior day).
- **When** the user navigates to the History screen and selects that date.
- **Then** the event is displayed as read-only. There is no tap-to-flip, no edit button, and
  attempting to manipulate it produces no change.

### 14.7 Sequential alarms

- **Given** two scheduled medications whose alarm times are both 08:00.
- **When** 08:00 arrives with the app open.
- **Then** exactly one alarm modal is shown at a time, and on resolving the first (Take or
  Ignore), the second modal opens immediately afterward.

### 14.8 Past-time alarm on app open

- **Given** a scheduled medication with alarm time 08:00 and no `DoseEvent` recorded for today.
- **When** the user opens the app at 10:00.
- **Then** a `pending` event is materialized for 08:00 today, and the alarm flow fires
  immediately (as per §7.3).

### 14.9 PRN manual log

- **Given** a PRN medication named "Ibuprofen".
- **When** the user presses **Took it now** and selects Ibuprofen.
- **Then** a new `DoseEvent` is created with `status = taken`, `scheduledTime = null`,
  `source = prn`, and it appears in today's timeline.

### 14.10 30-day retention

- **Given** a `DoseEvent` whose `date` is 30 days before today (i.e., just outside the
  window).
- **When** day rollover runs.
- **Then** the event is deleted from storage and no longer appears on the History screen.

### 14.11 Delete medication clears history

- **Given** a medication with dose events today and on prior days.
- **When** the user deletes the medication after confirming.
- **Then** the medication is gone from the Medications list, from today's timeline, and from
  every historical day's view, and no `DoseEvent` with its `medicationId` remains in storage.

### 14.12 Backgrounded tab still plays alarm

- **Given** the app is open in a tab but the user is focused on a different browser tab.
- **When** an alarm time arrives.
- **Then** the alarm sound plays audibly (subject to the browser's audio autoplay policy). The
  app does NOT change the tab title, show a browser notification, or flash the favicon.

### 14.13 Closed tab misses the alarm

- **Given** the app is not open in any tab.
- **When** an alarm time arrives.
- **Then** nothing happens — no sound, no notification. The `DoseEvent` remains `pending`
  until the user next opens the app, at which point §14.8 applies.

## 15. Definition of Done

The implementation is complete when all of the following are true. Each item is independently
verifiable against the spec.

- [ ] A user can create, view, edit, and delete medications with the full field set in §5.1
      and validation in §5.1.2.
- [ ] A medication can be marked PRN, and PRN medications have no alarm times and expose the
      **Took it now** affordance (§7.4).
- [ ] A scheduled medication can have one or more daily alarm times; all are validated as
      distinct and in ascending order (§5.2).
- [ ] The Today screen shows today's dose timeline and a persistent "Missed today" section
      that updates in real time (§10.2).
- [ ] When an alarm time arrives with the tab open, the built-in sound loops and a modal
      shows naming the medication (§8.1, §8.2).
- [ ] **Take** records the dose as taken and stops the sound (§8.4).
- [ ] **Ignore** records the dose as missed and does not re-ring before midnight (§8.4).
- [ ] Ignored doses on day D-1 do not carry over; on day D, a fresh `pending` event exists
      and rings at the scheduled time (§14.4).
- [ ] Concurrent alarms are processed sequentially in the ordering defined in §8.3.
- [ ] Past-time alarms at app open materialize and ring immediately (§7.3).
- [ ] Today's dose events can be flipped between `taken` and `ignored` by tapping, and the
      "Missed today" list reflects changes in real time (§9.2, §10.2.2).
- [ ] Dose events on any day before today are read-only (§9.3, §10.5).
- [ ] Day rollover at midnight ignores unresolved pending/due events, materializes today's
      events, purges events older than 30 days, and refreshes the UI (§6.3).
- [ ] Deleting a medication removes the medication and every `DoseEvent` referencing it,
      including history (§10.4.1).
- [ ] All state persists across page reloads via browser-local storage and the app works
      fully offline after initial load (§11).
- [ ] The app loads and behaves correctly on the latest two versions of Chrome, Edge, Firefox,
      and Safari, on desktop and mobile viewport widths (§12.1, §12.2).
- [ ] No network requests are made after initial page / asset load (§11.3).
- [ ] None of the out-of-scope features in §13 are present.

## Appendix A — Field Reference

Full reference of every stored field, defaults, and whether the UI exposes them.

| Entity | Field | Type | Required | Default | User-editable |
|---|---|---|---|---|---|
| Medication | id | string | yes | generated | no |
| Medication | name | string | yes | — | yes |
| Medication | doseAmount | string? | no | null | yes |
| Medication | doseUnit | string? | no | null | yes |
| Medication | form | enum? | no | null | yes |
| Medication | notes | string? | no | null | yes |
| Medication | isPRN | bool | yes | false | yes |
| Medication | alarmTimes | AlarmTime[] | yes | [] | yes (unless PRN) |
| Medication | createdAt | timestamp | yes | now() at create | no |
| Medication | updatedAt | timestamp | yes | now() at create/edit | no |
| AlarmTime | hour | int 0..23 | yes | — | yes |
| AlarmTime | minute | int 0..59 | yes | — | yes |
| DoseEvent | id | string | yes | generated | no |
| DoseEvent | medicationId | string | yes | — | no |
| DoseEvent | date | Date | yes | — | no |
| DoseEvent | scheduledTime | AlarmTime? | no | null for PRN | no |
| DoseEvent | status | enum | yes | pending (scheduled) / taken (prn) | indirectly (flip today only) |
| DoseEvent | statusChangedAt | timestamp | yes | now() on every transition | no |
| DoseEvent | source | enum | yes | scheduled / prn / manual-edit | no |

## Appendix B — Event Taxonomy

Runtime events the app produces internally. These are not persisted, but the UI and state
machine react to them. Implementers MAY expose these as hook points.

| Event | Triggered when |
|---|---|
| `dayRollover(date)` | Local clock crosses midnight, or app open detects missed rollover. |
| `alarmRaised(doseEventId)` | A `pending` → `due` transition occurs and no other alarm is active. |
| `alarmResolved(doseEventId, outcome)` | The user presses Take or Ignore (`outcome` ∈ {taken, ignored}). |
| `prnLogged(doseEventId)` | The user creates a PRN dose via **Took it now**. |
| `doseEdited(doseEventId, from, to)` | The user flips today's event between `taken` and `ignored`. |
| `medicationCreated(medicationId)` | User saves a new medication. |
| `medicationUpdated(medicationId)` | User saves edits to an existing medication. |
| `medicationDeleted(medicationId)` | User confirms deletion; includes cascade of all its DoseEvents. |
