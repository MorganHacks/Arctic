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

## Validation happens twice, and only one of them counts

The page checks required questions, email shape and number ranges before it
posts. That is a courtesy: it saves a round trip and puts each message beside
the box it belongs to.

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
