/**
 * The zone every date in this system is read and written in.
 *
 * Dates are stored as instants, and nobody thinks in instants. "Registration
 * opens January 15th at midnight" means midnight where the event is; rendered
 * in UTC it lands on the wrong calendar day for exactly the people it matters
 * to. MorganHacks happens at Morgan State University in Baltimore, so the
 * answer is Eastern.
 *
 * One answer, in one file. It was three before: the form builder's deadline,
 * the events screens, and the public form. Three copies of a constant is three
 * chances for the console to say registration opens at a time the public form
 * shows differently, with nothing failing to announce it.
 *
 * Named rather than an offset, so the standard and daylight switch stays the
 * platform's problem rather than becoming ours twice a year.
 */
export const ZONE = "America/New_York";
