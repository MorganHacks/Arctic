import { NoForm } from "./no-form";

/**
 * Anything that is not a form.
 *
 * There is no home page here — a form is only ever reached by its code — so
 * this covers `/` as well as any path with more segments than a code.
 */
export default function NotFound() {
  return <NoForm />;
}
