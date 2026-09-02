"use client";

import { useActionState } from "react";
import type { Profile } from "@/lib/api";
import { saveProfile } from "../../actions";

/** Shirt sizes come from the API, so the list cannot drift from what it accepts. */
const shirtLabels: Record<string, string> = {
  xs: "XS",
  s: "S",
  m: "M",
  l: "L",
  xl: "XL",
  "2xl": "2XL",
  "3xl": "3XL",
};

/**
 * The six fields, editable or not.
 *
 * When it is read-only the fields are still rendered and still filled in,
 * because seeing what we hold is the more common reason to open this page than
 * changing it — and a locked page that shows nothing answers neither question.
 * The explanation for the lock is above this form, on the page.
 *
 * Nothing here is trusted. The API refuses a write on a closed application
 * whatever this component renders, so `editable` is a courtesy rather than a
 * control.
 */
export function ProfileForm({
  profile,
  shirtSizes,
  editable,
}: {
  profile: Profile;
  shirtSizes: string[];
  editable: boolean;
}) {
  const [state, action, pending] = useActionState(saveProfile, {});

  return (
    <form action={action} className="panel">
      {state.error ? (
        <div className="notice problem">
          <p>{state.error}</p>
        </div>
      ) : null}

      {state.done ? (
        <div className="notice done">
          <p>Saved.</p>
        </div>
      ) : null}

      <fieldset
        disabled={!editable || pending}
        style={{ border: 0, margin: 0, padding: 0, minInlineSize: 0 }}
      >
        <div className="field">
          <label htmlFor="firstName">First name</label>
          <input
            id="firstName"
            name="firstName"
            defaultValue={profile.firstName ?? ""}
            autoComplete="given-name"
            maxLength={120}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="lastName">Last name</label>
          <input
            id="lastName"
            name="lastName"
            defaultValue={profile.lastName ?? ""}
            autoComplete="family-name"
            maxLength={120}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="school">School</label>
          <input
            id="school"
            name="school"
            defaultValue={profile.school ?? ""}
            autoComplete="organization"
            maxLength={120}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="shirtSize">Shirt size</label>
          <select
            id="shirtSize"
            name="shirtSize"
            defaultValue={profile.shirtSize ?? ""}
          >
            {/* Blank is a real answer. Somebody who does not want a shirt
                should not have to pick a size to save their dietary needs. */}
            <option value="">Not saying</option>
            {shirtSizes.map((size) => (
              <option key={size} value={size}>
                {shirtLabels[size] ?? size.toUpperCase()}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor="dietaryNeeds">Food and allergies</label>
          <textarea
            id="dietaryNeeds"
            name="dietaryNeeds"
            defaultValue={profile.dietaryNeeds ?? ""}
            maxLength={500}
            placeholder="Vegetarian, no nuts, anything we should know"
          />
          <p className="hint">
            This goes to whoever orders the food, and to nobody else.
          </p>
        </div>

        <div className="field">
          <label htmlFor="accessibilityNeeds">Access needs</label>
          <textarea
            id="accessibilityNeeds"
            name="accessibilityNeeds"
            defaultValue={profile.accessibilityNeeds ?? ""}
            maxLength={500}
            placeholder="Anything you need from us to take part"
          />
          <p className="hint">
            Tell us as much or as little as you like. We would rather know
            early than on the day.
          </p>
        </div>

        {editable ? (
          <div className="actions">
            <button type="submit" className="primary" disabled={pending}>
              {pending ? "Saving…" : "Save changes"}
            </button>
          </div>
        ) : null}
      </fieldset>
    </form>
  );
}
