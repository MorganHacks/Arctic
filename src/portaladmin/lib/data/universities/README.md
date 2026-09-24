# US university directory

Names and domains from [Hipo/university-domains-list](https://github.com/Hipo/university-domains-list), filtered to `alpha_two_code: US`. The snapshot revision is recorded in `us.json`; the original MIT license is included here.

This data is loaded on the server. Only the schools in the current breakdown reach the browser. Matching uses normalized full names and explicit aliases in `lib/schools.ts`; ambiguous abbreviations are left unmatched.

Set `LOGO_DEV_PUBLISHABLE_KEY` to a `pk_` key in `.env.local` or the server environment. School logos load directly from Logo.dev in the dashboard school breakdowns. Missing logos use a school icon.

See [Logo.dev attribution](https://www.logo.dev/docs/platform/attribution) for production attribution requirements.
