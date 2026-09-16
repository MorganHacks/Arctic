# Arctic — a design brief

Everything a designer needs to redesign the MorganHacks platform, written from
the code as it actually is on 15 September 2026.

This is not a wish list. It is a description of three working applications,
the states each screen can be in, and the reasons behind the decisions that
are already there. Where something looks plain, this document tries to say
whether that is neglect worth fixing or a decision worth keeping — because
both exist, and telling them apart is most of the job.

---

## 1. What this is

**Arctic** runs MorganHacks, a hackathon at Morgan State University in
Baltimore. Roughly four hundred students attend. The platform handles the
whole lifecycle: applications open, people apply, organizers review and
decide, accepted hackers RSVP, everybody turns up, they check in at a door
with a QR code, and for thirty-six hours the thing runs.

There are three separate front ends and two audiences.

| App | Audience | Where |
|---|---|---|
| **portaladmin** — the organizer console | ~20 organizers | `admin.morganhacks.com` |
| **portalweb** — the public site + hacker portal | ~400 applicants | `morganhacks.com`, `/portal` |
| **portalforms** — the public form renderer | anybody with a link | `forms.morganhacks.com` |

Behind them are three services: **atlas** (the API — identity, applications,
forms, campaigns), **harbor** (a reverse proxy), and **lark** (the mail
sender). A designer never touches these, but they decide what a screen can
know, so this document says where it matters.

### The shape of the year

Design that ignores this will be wrong. The platform has four distinct
seasons and the same screen is used very differently in each.

1. **Quiet months.** One or two organizers, setting up an event, building a
   form, writing email templates. Unhurried, on a laptop, often one field at
   a time after a meeting.
2. **Registration.** Applications arrive daily. The applicants list and the
   review flow are the whole product. Repetitive work, hundreds of rows.
3. **Decision week.** Accept, reject, waitlist. High-stakes, irreversible,
   done in batches, usually late at night by tired people.
4. **Event weekend.** Thirty-six hours. Phones, not laptops. Bad wifi. A door
   with a queue behind it. Announcements going out to four hundred people.
   This is when a slow or confusing screen costs something real.

The console is currently designed as though every day were season one.

---

## 2. Constraints that are not negotiable

A redesign that breaks any of these is not usable, however good it looks.

### 2.1 The theme system

There is a shared token file at `libs/ui/tokens.css`, used by all three apps.
It defines a **light and a dark palette**, and the dark one is not an
afterthought — organizers work at night during the event.

```
--paper   #fcfcfd   the page
--raised  #ffffff   a card sitting on it
--sunken  #f5f7f9   a well, an input, a code block
--ink     #14161a   primary text
--muted   #646d7a   secondary text
--faint   #8b939e   tertiary, timestamps
--line    #e3e7ec   borders
--line-soft #eef1f4 dividers inside a card

--accent      #2d5bd7  the thing to do next
--accent-ink  #ffffff  text on the accent
--accent-soft #eaf0ff  a tint of it

--ok    #1a7f52   +soft  landed, delivered, live
--warn  #8a6100   +soft  pending, expiring
--stop  #b23b4a   +soft  refused, failed, revoked

--focus #2d5bd7

--display  serif     headings
--body     system-ui  everything else
--mono     ui-monospace  keys, codes, addresses

--radius 4px   --radius-lg 6px
```

Three theme blocks, in this order, and the order is load-bearing:
`:root` (light) → `:root:not([data-theme="light"])` under
`prefers-color-scheme: dark` → `:root[data-theme="dark"]`. That third one is
what lets an explicit toggle beat the OS setting in both directions.

**You may propose a new palette.** You may not propose a design that only
works in one theme, or that hard-codes a colour where a token exists.

### 2.2 Motion

There are duration and easing tokens (`--dur-fast` 90ms, `--dur` 140ms,
`--dur-slow` 190ms, `--ease`) and a `prefers-reduced-motion` block that
collapses them. A looping animation must be switched off **by name** in that
block — the shared rule collapses durations, and a shimmer at 0.01ms is a
strobe rather than a stillness.

Motion is welcome. Motion that does not answer a question is not. The bar
used elsewhere in this codebase: *is what I am looking at current*, and *did
the thing I just pressed do anything*.

### 2.3 Hiding a control is a courtesy, never a boundary

Every screen checks permissions to decide what to show. **None of those
checks is security.** The API refuses the request whether or not the button
rendered. A design that assumes "they can't see it so they can't do it" is
wrong about this system, and the code says so in about a dozen comments.

Practical consequence: when a viewer lacks a permission, the screen shows a
**refusal that names the permission** (`You do not have people.view. Ask an
admin.`) rather than pretending the feature does not exist. Those refusals
are a real screen state you must design.

### 2.4 Accessibility already present — do not regress it

- A control that changes meaning must not change its **accessible name while
  focused**. Several buttons deliberately keep their label and announce the
  outcome in a separate `role="status"` element instead.
- Two-press confirmations move focus to the question, so a screen reader
  hears what is being asked rather than landing on a button that silently
  changed meaning.
- Tables are openable by keyboard: the date cell is a real button, because a
  table only a mouse can open is a table half the organizers cannot use.
- Colour is never the only signal. Every pill has a word in it.

### 2.5 Copy is signed off, not invented

Every user-visible sentence is approved by the project owner. There is a
`COPY: needs sign-off.` marker convention in the code. **Propose wording
freely — it is genuinely wanted — but mark it as a proposal.** Do not present
invented copy as final.

### 2.6 One thing you must not restyle

`components/templates/email-preview.tsx` renders the email being written
inside a sandboxed iframe. It injects exactly one CSS declaration
(`color-scheme: light`) and nothing else, deliberately. It used to inject a
typeface, line-height and padding, and those four rules made every preview a
lie about what would arrive in an inbox. **Style the frame around it. Never
style inside it.**

---

## 3. The organizer console (portaladmin)

Seventeen screens. A persistent top bar with the brand, the nav, the signed-in
identity and sign out. Nav items are filtered to what the viewer can open, so
a comms organizer sees five tabs and a super-admin sees seven.

### 3.1 `/` — the landing screen

Currently near-empty. **The single biggest opportunity in the console.**

An organizer signing in has a question that depends entirely on the season:
during registration it is "how many new applications"; on decision week it is
"how many are still undecided"; on event weekend it is "how many have checked
in". None of that is on screen anywhere. There is no dashboard.

### 3.2 `/events` and `/events/[id]`

The events everything else belongs to. First in the nav because it is first
in the work: a form belongs to an event, an applicant belongs to an event, a
mail segment is a question asked about one. **In an environment with no
event, every other screen is empty and none of them can say why.**

The detail screen edits dates and capacity. The name is editable; the slug is
not, because everything else refers to the event by it. There is also an
**announcements** panel here — posting to everybody at the event, immediately,
no draft, nobody emailed. Retracted announcements stay on the list struck
through, because the difference between "never posted" and "posted and taken
back" is the whole story of what people were told.

**States:** no events at all (first-run), one event, several with one current.

### 3.3 `/applicants` and `/applicants/[id]`

**The screen registration lives in.** It answers three questions and nothing
else: who has applied, where have they got to, which do I open next.

The detail screen puts identity across the top — because both columns beneath
are read against it, and a name in a right-hand rail scrolls away from the
answers it is the heading for. Answers on the left (the long column, what a
decision is made from), controls on the right.

The **status machine** has eleven states, and a designer needs all of them:

```
Incomplete → Submitted → UnderReview → Accepted   → Confirmed → CheckedIn
                                     → Rejected                → Declined
                                     → Waitlisted              → Expired
                                                                → Withdrawn
```

`Expired` is derived, not stored — an Accepted application past its RSVP
deadline reads as Expired. Eleven states is a lot of pills to distinguish; at
present they share three or four colours. This is a real design problem worth
solving properly.

**Sensitive:** résumés and PII sit behind separate permissions
(`applications.view_resume`, `applications.export`), and opening a résumé
leaves an audit mark. A design should make "this is on the record" legible
without being shrill.

### 3.4 `/forms`, `/forms/[id]`, `/forms/[id]/responses`

`/forms` answers three questions: what is the link, is it live, where do I
edit it. Nothing else — everything a form has beyond that belongs in the
builder.

**The builder** is the most complex screen in the product. One client
component: questions reorder, a live preview, drafts autosave, versions
publish. Question types include short text, long text, choice, multi-choice,
file upload, and **section breaks** that split a form into pages.

Forms have a **version history**. Publishing writes a new version; answers
are stored against the version the person saw, because a question reworded in
March makes an answer from February unreadable without it.

**Responses** shows submissions newest-first with keyset pagination, a CSV
export, and a per-response detail panel. As of this week it also shows
answers from surveys and from people who were not signed in — those are
marked `anon`.

### 3.5 `/mail`, `/mail/[id]`, `/templates/*`

Mail is split deliberately across two screens, and the split is a design
decision worth preserving.

`/mail` lists campaigns and creates them, because creating one is three
fields. **Sending happens on `/mail/[id]` and nowhere else**, because it is
the thing that cannot be taken back. That page shows the resolved recipient
count and a sample of real addresses before anything goes out.

The code says it plainly: *a screen whose only job is to slow somebody down
for ten seconds has to look different from the thirty screens that do not.*
Right now it does not look different enough. **This is the best single
opportunity for design to do safety work.**

Campaign states: `draft → queued → sending → sent`, plus `cancelled` and
`failed`.

**The template editor** (`/templates/new`, `/templates/[key]`) is a document
being written beside a live preview of itself. Fields: key (immutable once
set), subject, sender name, reply-to, body. The body is Markdown or HTML, and
the format switch does not convert between them — converting would mean
guessing, and a guess that rewrites somebody's body is worse than leaving it
alone.

Placeholders (`{{firstName}}`, `{{school}}`, `{{levelOfStudy}}`,
`{{graduationYear}}`, `{{lastName}}`) are offered by a `{{` menu and listed
under the editor, with unknown names marked — because a name the API does not
know is one that comes back refused, long after the person who typed it moved
on.

### 3.6 `/people`, `/people/[id]`, `/audit`

The permission model made visible. A person's page shows teams, individual
grants, and the union of the two — because the answer to "why can they do
this" is almost never in one of them alone. A lapsed membership stays on the
page, because it is the explanation for a permission that is gone.

Also here: revoke, restore, and unlink-Google, each behind a two-press
confirmation that names the address.

`/audit` is every access change in order, including ones since undone.

**Seven teams** exist: super-admin, registration, comms, logistics, judge,
volunteer, sponsorship. **Twenty-seven permissions.** Judges, volunteers and
sponsorship currently have *no screen in this console at all* — they hold
permissions for products that do not exist yet. That is a real gap a designer
should know about.

### 3.7 `/sign-in`

Google only, no password field — organizer access is an allowlisted address
plus a Google account bound on first sign-in. Three distinct refusal states,
each with different wording and a different action.

---

## 4. The hacker portal (portalweb)

Two things share one app: the **public marketing site** at `/` and the
**hacker portal** at `/portal`. They currently share a visual language and
probably should not — one is a brochure, the other is an account.

The portal is behind a feature flag and is currently **off in production**.

### 4.1 `/portal` — the home

*"Where a sign-in link lands, and the only screen most applicants will open."*

One question: **where is my application.** The answer is the first thing on
the page, in the words the API chose — nothing on this side maps a status,
because a second copy of that mapping would eventually disagree with the one
the team signed off.

Crucially, the portal **does not leak decisions**. An accepted applicant sees
"application received" until decisions are announced. Whatever you design
here has to hold that line.

### 4.2 `/portal/profile`

Name, school, shirt size, dietary needs, accessibility needs. **Not the
application** — everything a reviewer reads is fixed at submit.

This form **locks when a decision lands**, and the lock is deliberate: shirts
and catering are ordered from these answers. A designer should make the lock
legible and unresented — explain it, do not just disable the fields.

### 4.3 `/portal/resume`

Its own screen, not a field on the profile, because it behaves differently:
it uploads the moment a file is chosen, and unlike the profile it does **not**
lock when a decision lands. PDF only, 5MB cap.

### 4.4 `/portal/check-in`

**The one screen designed for a phone in a queue.** A QR code, with a quiet
zone of four modules and a light plate drawn behind it — because a reader
finds the symbol by the contrast at its edge, and in dark mode a QR on a dark
page is one a phone hunts for.

This screen is used once, for about four seconds, by someone who is probably
holding a bag. It is the most physically constrained thing in the product and
deserves attention as such.

### 4.5 `/portal/announcements` and `/portal/messages`

Two screens that look similar and answer different questions, and the
distinction is deliberate.

**Announcements** is what the team told everybody, newest first.
**Messages** answers exactly one question — *"you say you emailed me, did it
arrive?"* — about mail addressed to that one person. It shows delivery state
and withholds content.

Only two of the four delivery words get colour: "delivered" means stop
worrying, "could not be delivered" means check your spam folder. "Sending" is
neutral, because waiting is not a problem and colouring it spends the
reader's attention on nothing.

### 4.6 `/portal/sign-in`

No password field, because no password exists. A link to the address you
applied with is the whole of it.

---

## 5. The public form (portalforms)

`/{code}` — a form reached by a short code that goes on a flyer. Multi-page
when the form has section breaks, per-field validation returning **every
problem at once** keyed by field, an optional sign-in gate, and file upload.

`/{code}/thanks` — a real page, not a message swapped in, so it survives a
refresh and can be shown to somebody who asks "did it go through?".

This is the **highest-stakes surface in the product**: it is the first thing
a prospective hacker ever sees, often on a phone, often from a QR code on a
poster, and a form that looks untrustworthy is an application never started.
It currently gets the least design attention of anything here.

---

## 6. Where design can do the most work

Ranked, with reasons.

1. **The public form.** First impression, phone, highest drop-off risk.
2. **The console landing screen.** Currently almost empty; should answer the
   question the current season is asking.
3. **The send confirmation on `/mail/[id]`.** The only irreversible action
   in the product that reaches four hundred people. It should feel different
   from everything else and currently does not.
4. **The eleven application states.** Too many states, too few visual
   distinctions.
5. **Event-weekend screens on a phone** — check-in and announcements.
6. **The builder.** Powerful and dense; the place a designer can remove the
   most friction per hour spent.
7. **Empty and refusal states.** Every screen has them, they are written
   thoughtfully in words, and they are visually plain.

---

## 7. What to leave alone

Not sacred, but each has a reason. Argue with them by all means — with the
reason, not around it.

- **Sending on its own page, behind a preview of real addresses.**
- **The profile lock after a decision.** Catering and shirts depend on it.
- **No decision leakage in the portal before announcement.**
- **The format switch not converting Markdown to HTML.**
- **The immutable form key and event slug.**
- **Retracted announcements staying visible, struck through.**
- **Refusals naming the permission** rather than hiding the feature.
- **The email preview iframe carrying no styling of ours.**

---

## 8. What a good deliverable looks like

For each screen: the **loaded** state, the **empty** state, the **refused**
state, **loading**, and at least one **error**. Light and dark. Desktop and
phone for anything used during the event.

Plus:

- A revised token set, in the same shape as `libs/ui/tokens.css`, so it drops
  into three apps at once.
- A type scale that does something with the serif display face, which is
  currently barely used.
- Eleven application states as a coherent system, not eleven decisions.
- Motion specs as duration and easing against the existing tokens, with the
  reduced-motion behaviour stated for each.
- A component inventory: the pill, the panel, the listing row, the two-press
  confirmation, the empty state, the refusal, the table, the form field.

**Two audiences, one family.** The console is a tool for twenty people who
use it for months, and should be dense, fast and calm. The portal is for four
hundred people who open it five times, and should be warm, legible and
reassuring. They should look related. They should not look the same.
