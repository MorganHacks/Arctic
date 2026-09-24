import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Add01Icon } from "@hugeicons/core-free-icons";
import { NoTemplates } from "@/components/templates/no-templates";
import { TemplatesTable } from "@/components/templates/templates-table";
import styles from "@/components/templates/templates.module.css";
import { Icon } from "@/components/ui/icon";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { readTemplates } from "./api";

/**
 * Every email this system can send.
 *
 * Until this screen existed a template could only be written by hand in SQL,
 * which is why no campaign has ever gone out. The list is the whole of it:
 * what each one is called, which lane it sends down, and one press to open it.
 */
export default async function Templates() {
  const { person, data: templates } = await readPageData(() => readTemplates(true, true));

  if (!templates.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Templates</h1>
        <div className="empty">
          {templates.status === 403 ? (
            <>
              You do not have <code>email.manage_templates</code>. Ask an admin.
            </>
          ) : (
            templates.error
          )}
        </div>
      </Shell>
    );
  }

  // Cosmetic. The API refuses the write whether or not this link rendered, so
  // hiding it is a courtesy to somebody who cannot use it rather than a
  // control over anything.
  const canManage = person.permissions.has("email.manage_templates");

  return (
    <Shell personId={person.personId} templateCount={templates.items.length}>
      <div className="form-head">
        <div>
          <h1>Templates</h1>
          <p className="lede" style={{ margin: 0 }}>
            The subject, body and sending address of every email this system can
            send.
          </p>
        </div>

        {canManage ? (
          <Link href="/templates/new" className={`button primary ${styles.newTemplateButton}`}>
            <Icon icon={Add01Icon} size={16} />
            <span>New template</span>
          </Link>
        ) : null}
      </div>

      {/* Scaffolding, and says so. Goes with the fixtures in api.ts the moment
          the endpoints land. */}
      {templates.mocked ? (
        <p className="error">
          Showing example data. The templates API is not available yet.
        </p>
      ) : null}

      {templates.items.length === 0 ? (
        <NoTemplates />
      ) : (
        <TemplatesTable templates={templates.items} />
      )}
    </Shell>
  );
}
