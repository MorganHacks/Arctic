"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { usePathname, useSearchParams } from "next/navigation";
import { useId, useState } from "react";
import {
  ArrowRight01Icon,
  Audit02Icon,
  Calendar03Icon,
  FormIcon,
  InformationCircleIcon,
  Layout01Icon,
  Mail01Icon,
  Menu01Icon,
  UserAccountIcon,
  UserGroupIcon,
} from "@hugeicons/core-free-icons";
import { Icon, type IconSvgElement } from "@/components/ui/icon";
import type { Section } from "./sections";
import styles from "./sidebar.module.css";

function NavSection({ section, badge, pathname, collapsed, onNavigate }: {
  section: Section;
  badge?: number;
  pathname: string;
  collapsed: boolean;
  onNavigate?: () => void;
}) {
  const active = pathname === section.href || pathname.startsWith(`${section.href}/`);
  const [expanded, setExpanded] = useState(true);
  const submenuId = useId();
  const query = useSearchParams();
  const icon = sectionIcons[section.href] ?? Menu01Icon;

  if (collapsed || !section.children?.length) {
    return <Link href={section.href} className={styles.navItem} aria-label={section.label}
      title={collapsed ? section.label : undefined} aria-current={active ? "page" : undefined} onClick={onNavigate}>
      <Icon icon={icon} />
      <span className={styles.navLabel}>{section.label}</span>
      {!collapsed && badge !== undefined ? <span className={styles.navBadge} aria-label={`${badge} total`}>{badge}</span> : null}
    </Link>;
  }

  return <div className={styles.navSection}>
    <div className={`${styles.navItem} ${styles.navParent}`} data-active={active}>
      <span className={styles.navIconToggle}>
        <Icon icon={icon} className={styles.navSectionIcon} />
        <button type="button" className={styles.navExpand} aria-label={`${expanded ? "Collapse" : "Expand"} ${section.label}`}
          aria-expanded={expanded} aria-controls={submenuId} onClick={() => setExpanded(!expanded)}>
          <Icon icon={ArrowRight01Icon} size={18} className={styles.navChevron} />
        </button>
      </span>
      <Link href={section.href} className={styles.navParentLink} aria-current={active ? "page" : undefined} onClick={onNavigate}>
        {section.label}
      </Link>
      {badge !== undefined ? <span className={styles.navBadge} aria-label={`${badge} total`}>{badge}</span> : null}
    </div>
    <div id={submenuId} className={styles.navSubmenu} hidden={!expanded}>
      {section.children.map((child) => {
        const event = pathname === "/forms" ? query.get("event") : null;
        const href = event && child.href === "/forms#new-form" ? `/forms?${new URLSearchParams({ event })}#new-form` : child.href;
        return <Link key={child.href} href={href} className={styles.navSubItem} onClick={onNavigate}>
          <span>{child.label}</span>
        </Link>;
      })}
    </div>
  </div>;
}

const sectionIcons: Record<string, IconSvgElement> = {
  "/events": Calendar03Icon,
  "/people": UserGroupIcon,
  "/forms": FormIcon,
  "/applicants": UserAccountIcon,
  "/mail": Mail01Icon,
  "/templates": Layout01Icon,
  "/audit": Audit02Icon,
};

/** Sections are permission-filtered by the server; this only marks the route. */
export function Nav({
  sections,
  collapsed = false,
  onNavigate,
}: {
  sections: Section[];
  collapsed?: boolean;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  return (
    <nav className={styles.nav} aria-label="Primary">
      {sections.length === 0 ? (
        <p
          className={styles.noSections}
          title={
            collapsed ? "Nothing here yet. Ask an admin for access." : undefined
          }
        >
          <Icon icon={InformationCircleIcon} />
          <span className={collapsed ? styles.srOnly : styles.navLabel}>
            Nothing here yet. Ask an admin for access.
          </span>
        </p>
      ) : (
        sections.map((section) => {
          return <NavSection key={section.href} section={section} badge={section.badge} pathname={pathname}
            collapsed={collapsed} onNavigate={onNavigate} />;
        })
      )}
    </nav>
  );
}
