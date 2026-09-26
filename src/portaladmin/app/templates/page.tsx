import { TemplatesTable } from "@/components/templates/templates-table";
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
              You do not have permission to view templates. Ask an admin.
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
  const canDelete = person.permissions.has("email.delete_templates");

  return (
    <Shell personId={person.personId} templateCount={templates.items.length}>
      <TemplatesTable key={person.personId} templates={templates.items} personId={person.personId}
        initialHiddenKeys={templates.hiddenKeys}
        canManage={canManage} canDelete={canDelete && !templates.mocked} mocked={templates.mocked} />
    </Shell>
  );
}
