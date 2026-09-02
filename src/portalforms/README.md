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

## What is not here yet

- **File uploads.** The resume question renders and the filename and size are
  recorded, but there is no object store, so `resume_key` stays null and the
  bytes are not kept. A row with a filename and no key is the accurate record
  of that.
- **Surveys.** `kind = 'survey'` forms render, but submitting one is refused
  with a 501: there is no table for a survey answer to go in yet, and accepting
  the answers to drop them would leave somebody believing they had replied.
- **Saving a part-filled form.** The applications table has an `incomplete`
  status ready for it, but nothing here writes one until there is a way to
  identify who is coming back.
