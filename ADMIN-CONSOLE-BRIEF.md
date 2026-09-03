# MorganHacks organizer console — design brief

**For a designer producing mockups. Delete this file when you are done with it.**

Everything below describes software that exists and runs. Page inventory, field
names, permission strings and statuses were read out of the codebase, not
imagined. Where something is not built, it says so.

The product owner has **not** signed off the wording anywhere in the console.
Treat every string in this document as *placement guidance, not final copy*. If a
mockup needs a label, use the one here and mark it as unapproved.

---

## 1. What this is

Arctic runs MorganHacks: a hackathon at Morgan State University. The organizer
console is where the people running it work. It is **not** the applicant-facing
site — that is a separate app.

Three separate front ends exist:

| App | Domain | Who uses it |
|---|---|---|
| **Organizer console** ← *this brief* | `admin.morganhacks.com` | organizers |
| Public form | `forms.morganhacks.com/<code>` | applicants, mentors, anybody with a link |
| Hacker portal | `morganhacks.com` | accepted hackers |

The console is a **desktop-first working tool**. Organizers use it on a laptop,
often for hours, reviewing hundreds of rows. It is not a marketing surface and
should not look like one. Mobile should not break, but nothing here is designed
phone-first.

### The one team fact that shapes everything

**The organizing team turns over completely every year.** Every design decision
should assume the reader is new, has not been trained, and is doing this at 1am
the week registration opens. Prefer a screen that explains itself over one that
is elegant to somebody who already knows the system.

---

## 2. Visual system

A design system already exists at `libs/ui/tokens.css` and all three apps import
it. **Mockups should use these values.** They are not placeholders.

### Colour

```
Ground and ink            Light        Dark
  --paper    page         #fcfcfd      #0f1216
  --raised   cards        #ffffff      #161a20
  --sunken   wells        #f5f7f9      #0b0e12
  --ink      text         #14161a      #e8ebef
  --muted    secondary    #646d7a      #9aa3b0
  --faint    tertiary     #8b939e      #6b7480
  --line     borders      #e3e7ec      #262c34
  --line-soft            #eef1f4      #1d2229

Accent — the thing to do next, never emphasis
  --accent               #2d5bd7      #7fa0f5
  --accent-soft          #eaf0ff      #18243d

Status — never the same colour as "interactive"
  --ok       good        #1a7f52      #4fbd8b
  --warn     attention   #8a6100      #d0a545
  --stop     cannot undo #b23b4a      #e0798a
  (each has a -soft background variant)
```

**The governing rule, written at the top of the token file:**

> *Colour carries meaning or it is absent. Nothing is coloured for decoration.
> Somebody scanning a review queue for eight hours should be able to trust that
> anything coloured is telling them something.*

Follow this literally. A page with no problems on it should be almost entirely
ink on paper. If a mockup has colour spread across it for visual interest, it is
wrong.

**Hard constraint from the product owner: no orange, no black-and-orange, and
nothing resembling Claude's UI.** Both light and dark themes are required.

### Type

```
--display   serif    section and page headings
--body      system sans
--mono      for codes, ids, and anything in a numeric column

--step-2  0.75rem     --step1  1.125rem
--step-1  0.875rem    --step2  1.375rem
--step0   1rem        --step3  2rem
```

Use `font-variant-numeric: tabular-nums` anywhere digits line up in a column.

Radii are small: `4px`, `6px` for cards. This is a tool, not a consumer app.

---

## 3. RBAC — the part to get right

This is the most misunderstood area and the mockups need to show it, so it is
worth reading properly.

### How access is decided

Access is **never** decided by job title or team name in code. Every screen and
every API route checks for a **permission string** like `applications.decide`.

A person's effective permissions are:

```
  union of ( baseline permissions of every team they are in )
    ∪     ( individual grants made directly to them )
```

It is **purely additive**. There is no "deny" and no precedence order. Being in
two teams gives you both sets. Nothing subtracts.

Both team membership and individual grants **can carry an expiry**
(`expires_at`). This exists because judges should lose access the day after the
event rather than when somebody remembers to remove them.

### Why additive-only

A deny rule means "why can't I see this?" has an answer that requires reading
the whole ruleset in order. Additive means the answer is always "nothing has
given you it yet", which one screen can show.

### The teams, as seeded

| Team | Baseline permissions |
|---|---|
| **Super admin** | everything, including the two nobody else gets: `people.grant_permissions` and `audit.view` |
| **Registration** | `applications.view`, `applications.decide`, `applications.bulk_decide`, `applications.view_resume`, `applications.note`, `email.send_templated` |
| **Comms** | `email.send_broadcast`, `email.send_templated`, `email.manage_templates`, `email.view_stats`, `applications.view` |
| **Sponsorship** | `sponsors.view`, `sponsors.edit`, `email.send_templated` |
| **Logistics** | `applications.view`, `checkin.scan`, `swag.scan` |
| **Judge** | `judging.score_assigned` only |
| **Volunteer** | `checkin.scan`, `swag.scan` only |

Three deliberate choices worth showing in a mockup:

- **Comms can read applications but cannot decide them.** They need to build
  mailing segments; they must not accept anybody.
- **Logistics gets `applications.view` but not `applications.view_resume`.**
  They need headcount and dietary requirements. A CV is more sensitive than the
  rest of the record.
- **Volunteers get nothing that reads personal data in bulk.**

### Every permission that exists

```
applications.view            applications.view_responses   applications.view_resume
applications.decide          applications.bulk_decide      applications.note
applications.export          forms.manage

email.send_templated         email.send_broadcast
email.manage_templates       email.view_stats

people.view                  people.manage_teams           people.grant_permissions
audit.view

checkin.scan                 swag.scan                     checkin.view_stats
judging.score_assigned       judging.view_all              judging.assign
sponsors.view                sponsors.edit                 sponsors.view_financials
```

### How this must appear in the UI

Three rules the existing screens already follow, and mockups should keep:

1. **Navigation hides what you cannot use.** A Mail link that leads to a refusal
   is worse than no link.
2. **A refusal names the permission.** Not "access denied" — instead *"You do
   not have `applications.decide`. Ask an admin."* The reader can then ask for
   exactly the right thing. This matters enormously for a team that changes
   yearly.
3. **Partial data is normal, not an error.** Someone with `applications.view`
   but not `applications.view_resume` sees the applicant record with the resume
   section saying they cannot open it. The record does not 403 as a whole.

### Sign-in

Organizers sign in with **Google only** — there is no password field anywhere,
and being on the allowlist is what grants access. Applicants use emailed sign-in
links instead. A designer never needs to mock a password field.

---

## 4. The pages

Twelve routes exist, counting sign-in and the detail pages. Global chrome on
every one: a header with the MorganHacks wordmark, the nav, the signed-in
person, and a sign-out control.

The nav today reads **People · Forms · Applicants · Mail · Audit**. A
**Templates** link is being added with the template builder described in 4.8 —
mock it as present.

> Note: the header currently shows the signed-in person's **id**, not their
> email, deliberately — everything else in the system logs an id rather than
> personal data, and the header was not made the exception. A mockup may show a
> name if it looks better, but that is a change worth flagging.

---

### 4.1 `/forms` — Forms

The list of forms. A form is either an **application form** (the one that
creates applicants) or a **survey** (mentor sign-up, feedback, anything else).

Each row shows: name, kind, the **share code**, and status.

**The share code deserves design attention.** It is 7 characters from an
alphabet that deliberately excludes look-alikes — no `l`, `1`, `I`, `0`, `O`.
It gets read aloud at meetings and typed off slides, so it is rendered
lowercase, monospace, letter-spaced, with **no hyphen inserted** — a hyphen
added for readability is a hyphen somebody types into the URL bar.

Beside it: the full public address `forms.morganhacks.com/<code>` and a
one-press **Copy link**.

Status has three values, and the third is the one people forget:

- **Draft** — never published, the link shows nothing
- **Live · v3** — published, accepting answers
- **Closed** — published but past its deadline; the link still answers and tells
  applicants it has closed

Showing a closed form as "Live" is how a dead form ends up on a flyer, so these
must read differently at a glance.

**Create** asks for only two things — a **name** and a **kind** — because
everything else about a form is a question on it.

Permissions: `applications.view` to see the list, `forms.manage` to create or
publish. Empty state needed.

---

### 4.2 `/forms/[id]` — the form builder

A Google-Forms-style editor. Two panes: the question list, and a live preview of
what an applicant will see.

**Question types — eleven, plus one divider:**

```
Short text     Paragraph     Email        Phone       Number      Date
Dropdown       Choice        Checkboxes   Agreement   File upload
                                                      + Page break (section)
```

Per question: label, help text, a **required** toggle, and for the three choice
types an editable, reorderable option list.

**Reordering must work by keyboard, not only drag.** The current build uses
up/down buttons. Drag-only reordering is inaccessible and this screen is used
for hours.

Also present: **duplicate** a question and delete. Each question has a stable
**key** that answers are filed under, so it never changes when the wording does;
it is not shown on the question card, because it would be part of a row that
moves. The responses screens show it, which is where matching an export column
to a question actually happens.

**Page breaks** cut a long form into steps. Fields before the first break are
page one. A form with no breaks is a single page. Each break carries a heading
and an optional description, and the preview should show where the page divides.

**The questions a form starts with.** A new application form is created with a
standard set of questions already on it, so nobody types the ordinary ten by
hand. They are a starting point and nothing more: every one of them can be
reworded, retyped, reordered, duplicated or deleted, and neither the screen nor
the API argues. **Surveys start empty** — a mentor sign-up carries none.

**Reordering** moves. A question pressed up or down travels to its new place
rather than appearing in it, in under 200ms, and the two questions that swapped
both move because both of them did. Under `prefers-reduced-motion` it arrives
with no travel. The buttons keep focus, so a second press works immediately.

**Saving** is debounced with a persistent status showing `Saved` /
`Unsaved changes` / `Saving…` / `Not saved`, plus an explicit **Save now**. The
reader must never be unsure whether their work is safe.

**Publishing** validates, and shows **every problem at once** — not the first
one. Problems attach to the question they concern, with a separate panel for
form-wide ones. Publishing is what makes a version live; the old version keeps
serving until it does.

A **tab bar** switches between **Questions** and **Responses**.

---

### 4.3 `/forms/[id]/responses` — reading what came in

A dense table, newest first, paginated by a **Load more** that appends rather
than refetching.

Columns: submitted (UTC), version, one column per question, resume.

Three states that are normal and must not look broken:

- an older response **missing** a question added since → shown as unanswered
- a response holding an answer to a **deleted** question → its own column,
  marked *no longer on this form*
- a question that was never answered → a dash, not blank

Clicking a row opens the full response: every question with its answer,
including unanswered ones, plus the resume if attached. **Resume links are
signed and expire in about five minutes**, so they are fetched when the response
is opened, not when the list loads. Worth surfacing that expiry in the UI.

**Export CSV** downloads everything.

Permissions: `applications.view_responses` to read, `applications.export` for
the CSV, `applications.view_resume` for the file. A designer should mock the
in-between state where someone can read answers but not open resumes.

Empty state matters more than usual — this screen sits empty for weeks before
registration opens.

---

### 4.4 `/applicants` — the registration team's home

Where the registration team will actually live. Must stay usable at several
thousand rows.

**Filters:** an event picker, free-text search over name and email, and status
filters. Status counts are shown event-wide, not filtered — so you can see "412
submitted, 38 accepted" while looking at one slice.

**Columns:** name, email, school, status, submitted (UTC), whether a resume
exists.

**Eleven statuses**, exactly as the database constrains them:

```
Incomplete   Submitted   Under review   Accepted   Waitlisted   Rejected
Confirmed    Declined    Expired        Checked in  Withdrawn
```

This is where the colour rule earns its keep. These are not eleven colours.
Group them: in-progress states are neutral, `Accepted`/`Confirmed`/`Checked in`
read as `--ok`, `Rejected`/`Declined`/`Expired`/`Withdrawn` read as `--stop` or
muted, `Waitlisted` as `--warn`. Show a designer's grouping rather than a
rainbow.

Paginated by keyset with **Load more** and an `{n} loaded` counter.

---

### 4.5 `/applicants/[id]` — one applicant

Everything about one person, on one screen:

- **Identity and status**, with the lifecycle dates: started, submitted,
  decided, RSVP by, confirmed, declined, checked in
- **Their answers**, joined to the question labels of the version they answered
- **Resume**, behind a signed link, with its expiry stated
- **History** — every status change, who made it, when, and why
- **Notes** — organizer-only, free text

**Changing status is constrained.** There is a legality table: some statuses are
terminal (`rejected`, `declined`, `checked_in`, `withdrawn`) and the only route
out of `expired` is a manual reinstatement to `accepted`. The UI offers only
legal next states. When there are none, it says so plainly — the existing
wording is:

> *Nowhere left to go. Reversing this would be a new application rather than an
> edit, so that the history keeps saying what happened.*

A **reason** can accompany a change and is capped at 500 characters; anything
longer belongs in a note.

Every status change is written to an audit trail by a database trigger. This is
not optional and cannot be bypassed by the UI.

Permissions: `applications.view` to read, `applications.decide` to change
status, `applications.note` for notes, `applications.view_responses` for
answers, `applications.view_resume` for the file. **Each section degrades
independently** — mock at least one partial view.

---

### 4.6 `/mail` — campaigns

Mass email to applicants. The model has three parts and the UI should make them
legible, because this is the piece organizers find most confusing:

- **Template** — the email itself: subject, body, sending address. Written once,
  reused.
- **Segment** — who receives it.
- **Campaign** — a template plus a segment plus a name. Created as a draft.

**Three segment kinds only** — deliberately not a query builder:

1. **Applicants by status** — pick an event and one or more statuses
2. **Form respondents** — everyone who answered a given form
3. **Address list** — pasted addresses, one per line

**Statuses:** draft · queued · sending · sent · cancelled · failed.

Recipient count is blank on a draft, not `0` — nothing has been resolved yet.

---

### 4.7 `/mail/[id]` — the send screen

**This screen exists to prevent a mistake that cannot be undone.** Several
hundred emails cannot be recalled. Design it accordingly.

The flow is deliberately two-stage:

1. A draft offers **one** button: **Preview recipients**.
2. Only after the resolved count and a sample of addresses are on screen does a
   **Send** button exist at all. It then asks once more, naming the number.

The confirmation uses `--stop`, not the accent — this is "cannot be taken back",
not "the next thing to do".

Preview shows more than a count: **"412 matched, 400 will be sent, 12
suppressed"** with the reasons. Suppressed recipients are recorded, not
discarded, so somebody can go and find the twelve.

**Two-person approval.** A campaign cannot be sent by the person who created it.
The API refuses and the screen explains why. Mock this state — it is the one
that will confuse people first.

Once sent, the send controls **disappear** rather than grey out. Cancel is
available on a queued campaign, behind the same two-press confirmation.

Permissions: `email.view_stats` to read, `email.send_broadcast` to send or
cancel.

---

### 4.8 `/templates` — the template builder *(being built now)*

Where the actual emails are written. Currently the only way to create one is
hand-written SQL, which is why nothing can be mass-mailed yet.

- **List** of templates: key, subject, kind, version, last updated
- **Editor**: key, kind, subject, from address, reply-to, and a **Markdown**
  body
- **Live preview** rendered in a sandboxed frame so email HTML cannot inherit
  the console's styling and mislead the author
- **Placeholders** — `{{firstName}}` and similar — listed live as they are used,
  so an author sees what the email needs before a send refuses it

**Be honest about the medium in the UI.** Markdown and a small safe HTML subset
work. **JavaScript never runs in email** — every client strips it — and most CSS
does not survive either. One short line in the editor, not a lecture.

Templates are either **transactional** (sign-in links) or **broadcast**.
Campaigns can only use broadcast ones, so the campaign picker must only offer
those.

Editing bumps a version. A campaign already sent keeps the wording it had — each
message stores its own rendered subject and body — so an edit never rewrites
history, but it does mean the template and an old email can differ. The editor
says so behind a confirmation.

Permission: `email.manage_templates`.

---

### 4.9 `/people` — organizers, teams and grants

Where RBAC is administered. Two kinds of person exist: **organizers** (Google
sign-in, on the allowlist) and **hackers** (emailed links).

The list shows everyone with search and a kind filter.

**Adding an organizer** takes an email address. Being on the list is what grants
access — there is no invitation to accept.

**One person's page** shows:

- their **team memberships**, each addable and removable, each able to carry an
  **expiry**
- their **individual grants** — single permissions given directly, also with
  optional expiry
- their **effective permissions**: the computed union

That last one is the most useful thing on the screen and should be prominent. It
answers "why can this person do that?" without anybody reasoning about the
rules. **Ideally it shows provenance** — this permission comes from the
Registration baseline, that one from a direct grant expiring on the 14th.

**Revoke** ends someone's access entirely and is the most destructive control in
the console — treat it as such visually, with a two-press confirmation.

Permissions: `people.view` to read, `people.manage_teams` to change teams,
`people.grant_permissions` to grant directly. The last is super-admin only.

---

### 4.10 `/audit` — the trail

A read-only log: who did what, to what, when. Super-admin only (`audit.view`).

Enforced by database triggers rather than application code, so it cannot be
bypassed. A hand-written database change still records itself — with a null
actor, which is honest and permanently unattributable.

Design need: filterable, scannable, and clear about **what changed**, not just
that something did.

---

## 5. Not built — do not mock

Do not invent screens for these. Some have permissions seeded but no UI at all:

- **Judging** (`judging.*`) — no screens exist
- **Sponsors** (`sponsors.*`) — no screens exist
- **Check-in / swag scanning** (`checkin.*`, `swag.*`) — no screens exist
- **Bulk decisions** — `applications.bulk_decide` is seeded and used nowhere
- **A review queue** — deliberately deferred
- **Survey responses** — surveys can be built and published, but submitting to
  one is refused; only application forms store answers

---

## 6. Priorities for the mockups

If time is limited, in this order:

1. **`/applicants` and `/applicants/[id]`** — where the registration team lives
2. **`/forms/[id]` builder** — the most complex screen, most in need of design
3. **`/mail/[id]` send screen** — where design prevents a real disaster
4. **`/people/[id]`** — effective permissions with provenance
5. Everything else

Each ideally in **light and dark**, and at least one screen showing a **partial
permission** state, because that is the state a designer would otherwise never
think to draw and it is common in practice.

---

## 7. Reference

- Design tokens: `libs/ui/tokens.css`
- Team baselines: `src/atlas/MorganHacks.Migrations/Scripts/0002_teams.sql`
- Permission list: `src/atlas/MorganHacks.Identity/Domain/Permission.cs`
- Statuses and transitions: `0004_applications.sql`, `StatusTransition.Allowed`
- Existing screens: `src/portaladmin/app/`

To see it running: `deploy/local/dev.sh you@morgan.edu` starts everything and
opens the console signed in.
