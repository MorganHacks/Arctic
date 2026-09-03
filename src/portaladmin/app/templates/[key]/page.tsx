import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { Editor } from "@/components/templates/editor";
import styles from "@/components/templates/templates.module.css";
import { kindLabel } from "@/components/templates/types";
import { currentPerson } from "@/lib/api";
import { Shell } from "../../shell";
import { readPlaceholders, readTemplate } from "../api";

/**
 * One template, open.
 *
 * The key is the heading because it is what the template is: campaigns are
 * stored against it, it cannot be renamed, and the subject is a thing it says
 * rather than a thing it is.
 */
export default async function TemplatePage({
  params,
  searchParams,
}: {
  params: Promise<{ key: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const [{ key }, query] = await Promise.all([params, searchParams]);

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  /*
   * The campaign this template is being edited for, where there is one.
   *
   * A template is normally written before anybody has decided who it goes to,
   * so the general list is the ordinary answer and this is the exception:
   * opened from a campaign, the editor should only offer the names that
   * campaign's segment can actually fill. Nothing in the console links here
   * with it yet — see the note in the pull request.
   */
  const campaign =
    typeof query.campaign === "string" && query.campaign !== ""
      ? query.campaign
      : null;

  // Not decoded again. The router hands over a decoded segment, and a second
  // pass would quietly rewrite any key with a percent sign in it.
  //
  // Both reads are started together. The placeholders do not depend on the
  // template, and awaiting them in turn would put a second round trip in front
  // of a page that already waits on one.
  const [read, names] = await Promise.all([
    readTemplate(key),
    readPlaceholders(campaign),
  ]);

  if (!read.ok) {
    if (read.status === 404) {
      notFound();
    }

    return (
      <Shell personId={person.personId}>
        <h1>Template</h1>
        <div className="empty">
          {read.status === 403 ? (
            <>
              You do not have <code>email.manage_templates</code>. Ask an admin.
            </>
          ) : (
            read.error
          )}
        </div>
      </Shell>
    );
  }

  const { template } = read;

  return (
    <Shell personId={person.personId}>
      <Link href="/templates" className="back">
        ← Templates
      </Link>

      <div className="form-head">
        <div>
          <h1 className="mono">{template.key}</h1>
          <p className="lede" style={{ margin: 0 }}>
            <span className={styles.chip}>{kindLabel(template.kind)}</span>
            <span className="meta"> Version {template.version}</span>
          </p>
        </div>
      </div>

      {/* Scaffolding, and says so. Goes with the fixtures in api.ts the moment
          the endpoints land. */}
      {read.mocked ? (
        <p className="error">
          Showing example data. The templates API is not available yet.
        </p>
      ) : null}

      <Editor
        template={template}
        canManage={person.permissions.has("email.manage_templates")}
        available={names.ok ? names.items : null}
      />
    </Shell>
  );
}
