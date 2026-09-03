# portalforms

The public forms site. `forms.morganhacks.com/<code>` renders the published
version of one form and takes the answers.

A separate deployment from `portalweb` and `portaladmin`, and the reason is the
threat model rather than tidiness. This is the only one of the three that is
open to the internet with no sign-in at all, and the only one that accepts
writes from anonymous callers. It holds no session and reads no PII back.

## The code is the whole permission

A form's code is seven characters from an alphabet with no `0`, `O`, `1` or `l`,
generated randomly. Having the link is what makes somebody allowed to fill the
form in, which is why:

- every page here is `noindex, nofollow` — a crawler that finds a code and
  publishes it turns an unlisted form into a public one;
- an unknown code, a mistyped code and a form that is still a draft all get the
  same page. Telling them apart is a way to find out which codes are real;
- a closed form still resolves and says it closed. Somebody following a link
  off a flyer in March gets told the deadline passed, not a 404 they report as
  broken.

## A section cuts the form into steps

A field of type `section` is a heading, not a question. It is never answered,
never sent, and takes no number. What it does is mark where one step of the
form ends and the next begins:

- fields before the first section are step one;
- a section begins each step after that;
- a form with **no** sections is a single page, exactly as it has always been.

Most forms have none and should not be able to tell this exists. The
application form has fifteen questions, and fifteen questions on one page is a
wall — on a phone it is a scroll with no end in sight and no sense of how much
is left.

A section with nothing under it still gets a step. That is a heading and a
description with nothing to fill in, which is a legitimate thing to want — an
introduction, or instructions before the questions start — and dropping it
would delete something somebody wrote.

**Only the step on screen is checked.** Somebody on step one is never told
about a required question on step four: it is not on their screen, there is
nothing they can do about it from there, and a Next button that refuses for a
reason nobody can see is a form that looks broken. Every problem on *this* step
is raised at once, though, for the same reason it always was.

The last step checks the whole form before it posts — everything behind it has
already passed its own step, so this only catches somebody who walked back and
emptied a box. If it finds something, they are taken to the step it is on. Same
for a problem the API raises: whichever step the question lives on is the step
they land on, because a complaint about a question you cannot see is not one
you can act on.

**Answers are never lost by moving.** Every answer for every step lives in one
object for the whole form. Only the current step is mounted, so leaving one
costs nothing and coming back puts every box exactly as it was left — including
on steps nobody has reached yet.

### The browser's Back button is not a step

Going back a step is the on-screen **Back** button. The browser's own Back
button leaves the form, and the unload guard makes the browser ask first.

This was decided rather than defaulted to. Pushing a history entry per step is
what a phone user's thumb expects — but every way of doing it hands the App
Router a navigation, and a navigation this page does not survive takes every
answer with it. `router.push` re-runs the server component, which loads the
form `no-store`. Raw `pushState` avoids that until a `popstate` arrives for an
entry the router did not create.

The worst case of what is here is somebody swiping back out of habit and being
asked whether they meant it. The worst case of the other choice is a
fifteen-question application gone, silently, with nothing on screen to say why.

### One progress indicator, not two

A form in steps says `Step 2 of 4` and shows a bar for it. A form on one page
keeps the `n of m answered` count it has always had, above eight questions.
They are the same object measuring different things and they never appear
together: on one page every question is on screen and the useful number is how
many are done; in steps the useful number is which step.

The count lives inside the step's heading, and the heading is what takes focus
when a step changes. That makes one announcement — "Step 2 of 4, About you" —
where a live region would have talked over whatever somebody was reading.
Focus and scroll both go to the top on every step, because a silent swap of
every question on the page is disorienting for anybody not looking at the whole
screen.

## Validation happens twice, and only one of them counts

The page checks required questions, email shape and number ranges before it
posts. That is a courtesy: it saves a round trip and puts each message beside
the box it belongs to.

Every problem is raised at once, in two places. Each message sits under its own
question, and all of them are also listed at the top of the form — which is the
part that takes focus when a submission is refused, and the only part that
answers "how much is left to fix". A cursor dropped in the first bad box answers
"what is wrong here" and nothing else, and on a thirty-question form that is six
scrolls to find out there were six problems.

Nothing this side is ever stricter than the API. A rule that refuses an answer
the server would have accepted is worse than no rule at all, because the person
it refuses has no way past it — which is why the address check here only looks
for an `@` between two non-empty halves, and why the character caps are the same
numbers `SubmissionValidation` applies.

The API validates the same things against the version it loaded itself, and
that is the check that decides anything. It never reads the question list from
the request — a field list that arrived with the answers would be a claim
validating itself, and "this question was optional" would be true whenever the
caller said so.

## The API is served from this app's own origin

`next.config.ts` rewrites `/api/*` to harbor, the same as `portaladmin`.

There is no session cookie here, so the `SameSite=Lax` reasoning that drives it
over there does not apply — but it lands in the same place. A cross-origin API
would need harbor's hostname in the page, an `Access-Control-Allow-Origin`
entry per environment, and a preflight before every submission. Those are
things to get wrong on the one night of the year when several hundred people
are applying at once, and getting them wrong looks like a form that silently
will not submit.

**`API_ORIGIN` is read at build time, not at run time.** Next compiles rewrites
into the routes manifest during `next build`, so setting it only when starting
the server does nothing — the build's value is already baked in.

## The resume goes up before the form is submitted

A file uploads the moment it is picked, to `POST /api/forms/<code>/resume`,
rather than riding along with the answers. Somebody on a phone then spends the
upload while they are still answering questions instead of watching a progress
bar after pressing Submit, and on campus wifi that is the difference between a
submission that lands and one somebody gives up on. The Submit button is
disabled while a file is still going up, and says so.

**What comes back is an id, never a location.** The page is told
`{ upload: "<uuid>" }` and repeats exactly that at submit. It never learns the
storage key, because a key the browser could repeat would be a caller naming a
blob — the same shape of mistake as a field list that arrives with the answers.
The id names a row the API wrote, so the API can check it: issued for this
form, and not already spent on somebody else's application.

The page checks the size and that the file looks like a PDF before uploading.
Both are courtesies — they save pushing five megabytes up a slow connection to
be refused. The API reads the first bytes of the file and refuses anything that
does not really start `%PDF-`, which is the check that decides anything: a
`.pdf` on the end of a filename is a claim, and costs nothing to write.

## Nothing is cached

`loadForm` is `no-store`. A form closes at a moment somebody chose, and a new
version can be published while people are part-way through the old one. A
cached copy shows an applicant questions that are no longer the ones being
asked, and their answers then get stored against a version they were never
given.

## Running it

```bash
npm install
API_ORIGIN=http://localhost:5080 npm run dev
```

`API_ORIGIN` points at harbor. There is nothing at `/` — a form is only ever
reached by its code, so open `http://localhost:3000/<code>` for a form that
exists.

Uploading a resume needs Azurite, which `docker compose up` starts. Without it
the API answers 503 and says so; the rest of the form still works.

### Looking at the steps without an API that serves sections

Scaffolding, and it is meant to be deleted. `lib/preview.ts` makes up a form
with sections in it so the multi-step page can be looked at before the API can
serve one:

```bash
FORMS_PREVIEW=1 npm run dev
# /preview      — a form in five steps
# /previewflat  — the same questions with no sections, i.e. the single page
```

Two locks, both of which have to be open: `FORMS_PREVIEW=1`, and `NODE_ENV` not
being `production`, so a shipped build can never serve it. The copy in it is
placeholder and has not been approved. Delete the file and the marked block in
`lib/api.ts` once a real form has sections.

## What is not here yet

- **Cleaning up abandoned uploads.** Closing the tab half way through leaves a
  stored file no application points at. `applications.resume_uploads` has the
  index a sweeper will read; until somebody writes one, those are a storage
  bill rather than a correctness problem.
- **Surveys.** `kind = 'survey'` forms render, but submitting one is refused
  with a 501: there is no table for a survey answer to go in yet, and accepting
  the answers to drop them would leave somebody believing they had replied.
- **Saving a part-filled form.** The applications table has an `incomplete`
  status ready for it, but nothing here writes one until there is a way to
  identify who is coming back.
