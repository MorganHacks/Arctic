"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { usePathname } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  ArrowUp01Icon,
  Cancel01Icon,
  LayoutAlignLeftIcon,
  Logout03Icon,
  UserCircleIcon,
} from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Avatar } from "@/components/ui/avatar";
import { Nav } from "./nav";
import { useSidebarState } from "./sidebar-state";
import type { Section } from "./sections";
import styles from "./sidebar.module.css";

type Identity = { label: string; email: string | null; avatarUrl: string | null };
type SidebarProps = { sections: Section[]; identity: Identity };
const mobileQuery = "(max-width: 48rem)";
const SidebarPageContext = createContext<((props: SidebarProps) => void) | null>(null);

export function SidebarPage({ sections, identity, children }: SidebarProps & { children: React.ReactNode }) {
  const updateSidebar = useContext(SidebarPageContext);

  useLayoutEffect(() => {
    updateSidebar?.({ sections, identity });
  }, [updateSidebar, sections, identity]);

  return <>{children}</>;
}

function SidebarContents({
  sections,
  identity,
  collapsed = false,
  mobile = false,
  onToggle,
  onNavigate,
}: SidebarProps & {
  collapsed?: boolean;
  mobile?: boolean;
  onToggle: () => void;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const [accountOpen, setAccountOpen] = useState(false);
  const [toggleTooltip, setToggleTooltip] = useState<{ left: number; top: number } | null>(null);
  const accountRef = useRef<HTMLDivElement>(null);
  const accountButton = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  const tooltipId = useId();
  const toggleLabel = mobile
    ? "Close navigation"
    : collapsed
      ? "Expand sidebar"
      : "Collapse sidebar";

  useEffect(() => {
    setAccountOpen(false);
    setToggleTooltip(null);
  }, [pathname, collapsed]);

  useEffect(() => {
    if (!toggleTooltip) return;
    const hideTooltip = () => setToggleTooltip(null);
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") hideTooltip();
    };
    window.addEventListener("resize", hideTooltip);
    window.addEventListener("scroll", hideTooltip, true);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("resize", hideTooltip);
      window.removeEventListener("scroll", hideTooltip, true);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [toggleTooltip]);

  function showToggleTooltip(event: React.SyntheticEvent<HTMLButtonElement>) {
    if (mobile) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const edge = collapsed
      ? rect.right
      : (event.currentTarget.closest("aside")?.getBoundingClientRect().right ?? rect.right);
    setToggleTooltip({ left: edge + 8, top: rect.top + rect.height / 2 });
  }

  useEffect(() => {
    if (!accountOpen) return;
    function onPointerDown(event: PointerEvent) {
      if (
        event.target instanceof Node &&
        !accountRef.current?.contains(event.target)
      ) {
        setAccountOpen(false);
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        setAccountOpen(false);
        accountButton.current?.focus();
      }
    }
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    const media = window.matchMedia(mobileQuery);
    const closeAccount = () => setAccountOpen(false);
    media.addEventListener("change", closeAccount);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
      media.removeEventListener("change", closeAccount);
    };
  }, [accountOpen]);

  return (
    <>
      <div className={styles.sidebarTop}>
        <div className={styles.brandRow}>
          <Link
            href="/"
            className={styles.workspace}
            aria-label="MorganHacks account overview"
            onClick={onNavigate}
          >
            <span className={styles.workspaceMark} aria-hidden="true">
              M
            </span>
            <span className={styles.workspaceName}>MorganHacks</span>
          </Link>
          <button
            type="button"
            className={styles.iconButton}
            onClick={() => {
              setToggleTooltip(null);
              onToggle();
            }}
            onMouseEnter={showToggleTooltip}
            onMouseLeave={() => setToggleTooltip(null)}
            onFocus={showToggleTooltip}
            onBlur={() => setToggleTooltip(null)}
            aria-label={toggleLabel}
            aria-describedby={toggleTooltip ? tooltipId : undefined}
            aria-expanded={mobile ? undefined : !collapsed}
            aria-keyshortcuts={mobile ? undefined : "Meta+B Control+B"}
            title={mobile ? toggleLabel : undefined}
          >
            <Icon
              icon={mobile ? Cancel01Icon : LayoutAlignLeftIcon}
              size={20}
            />
          </button>
          {toggleTooltip ? createPortal(
            <div
              id={tooltipId}
              role="tooltip"
              className={styles.toggleTooltip}
              style={toggleTooltip}
            >
              {collapsed ? "Open sidebar" : "Close sidebar"}
            </div>,
            document.body,
          ) : null}
        </div>
        <Nav
          sections={sections}
          collapsed={collapsed}
          onNavigate={onNavigate}
        />
      </div>

      <div className={styles.sidebarBottom}>
        <div className={styles.account} ref={accountRef}>
          {accountOpen ? (
            <div id={menuId} className={styles.accountMenu}>
              <div className={styles.accountMenuInner}>
                <Link
                  href="/"
                  className={styles.menuItem}
                  onClick={() => {
                    setAccountOpen(false);
                    onNavigate?.();
                  }}
                >
                  <Icon icon={UserCircleIcon} />
                  <span>Your account</span>
                </Link>
                <div className={styles.menuDivider} />
                <form action="/api/auth/logout" method="post">
                  <button
                    type="submit"
                    className={`${styles.menuItem} ${styles.signOut}`}
                  >
                    <Icon icon={Logout03Icon} />
                    <span>Sign out</span>
                  </button>
                </form>
              </div>
            </div>
          ) : null}
          <button
            ref={accountButton}
            type="button"
            className={styles.accountButton}
            aria-label={`Account menu for ${identity.email ?? identity.label}`}
            aria-expanded={accountOpen}
            aria-controls={accountOpen ? menuId : undefined}
            title={collapsed ? (identity.email ?? identity.label) : undefined}
            onClick={() => setAccountOpen(!accountOpen)}
          >
            <Avatar
              name={identity.label}
              email={identity.email}
              avatarUrl={identity.avatarUrl}
              className={styles.avatar}
            />
            <span className={styles.userInfo}>
              <span className={styles.userName}>{identity.label}</span>
              <span className={styles.userEmail}>
                {identity.email ?? "Signed in"}
              </span>
            </span>
            <Icon
              icon={ArrowUp01Icon}
              size={16}
              className={`${styles.accountArrow} ${accountOpen ? styles.flipped : ""}`}
            />
          </button>
        </div>
      </div>
    </>
  );
}

/** Interactive chrome around server-rendered, permission-gated page content. */
export function SidebarLayout({
  sections,
  identity,
  children,
}: SidebarProps & { children: React.ReactNode }) {
  const pathname = usePathname();
  const [pageSidebar, setPageSidebar] = useState<SidebarProps | null>(null);
  const sidebar = pageSidebar ?? { sections, identity };
  const owner = sidebar.identity.email ?? sidebar.identity.label;
  const hasTemplates = sidebar.sections.some((section) => section.href === "/templates");
  const suppliedCount = sidebar.sections.find((section) => section.href === "/templates")?.badge;
  const [templateCount, setTemplateCount] = useState<{ owner: string; value: number } | null>(null);
  const knownCount = templateCount?.owner === owner ? templateCount.value : undefined;
  const navSections = useMemo(() => sidebar.sections.map((section) => section.href === "/templates"
    ? { ...section, badge: suppliedCount ?? knownCount }
    : section), [sidebar.sections, suppliedCount, knownCount]);
  const { collapsed, toggleCollapsed } = useSidebarState();
  const [mobileOpen, setMobileOpen] = useState(false);
  const mobileDialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (suppliedCount !== undefined) setTemplateCount({ owner, value: suppliedCount });
  }, [owner, suppliedCount]);

  useEffect(() => {
    if (!hasTemplates) {
      setTemplateCount(null);
      return;
    }
    const controller = new AbortController();
    fetch("/api/admin/templates?includeDrafts=true", { signal: controller.signal })
      .then((response) => response.ok ? response.json() : null)
      .then((body: { templates?: unknown[] } | null) => {
        if (!controller.signal.aborted && body?.templates) {
          const value = body.templates.length;
          setTemplateCount((current) => current?.owner === owner ? current : { owner, value });
        }
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [hasTemplates, owner]);

  const closeMobile = useCallback(() => mobileDialog.current?.close(), []);
  const openMobile = useCallback(() => {
    mobileDialog.current?.showModal();
    setMobileOpen(true);
  }, []);

  useEffect(() => {
    closeMobile();
  }, [pathname, closeMobile]);

  useEffect(() => {
    const media = window.matchMedia(mobileQuery);
    const onResize = () => {
      if (!media.matches) closeMobile();
    };
    media.addEventListener("change", onResize);
    return () => media.removeEventListener("change", onResize);
  }, [closeMobile]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!(event.metaKey || event.ctrlKey) || event.key.toLowerCase() !== "b")
        return;
      const target = event.target;
      if (
        target instanceof HTMLElement &&
        (target.isContentEditable || target.closest("input, textarea, select"))
      )
        return;
      event.preventDefault();
      if (window.matchMedia(mobileQuery).matches) {
        if (mobileDialog.current?.open) closeMobile();
        else openMobile();
      } else {
        toggleCollapsed();
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [closeMobile, openMobile, toggleCollapsed]);

  if (pathname === "/sign-in") return <>{children}</>;

  return (
    <SidebarPageContext.Provider value={setPageSidebar}>
      <div className={styles.frame}>
        <a className={styles.skipLink} href="#main-content">
          Skip to content
        </a>
        <aside
          className={styles.sidebar}
          data-collapsed={collapsed}
          aria-label="Console sidebar"
        >
          <SidebarContents
            sections={navSections}
            identity={sidebar.identity}
            collapsed={collapsed}
            onToggle={toggleCollapsed}
          />
        </aside>
        <div className={styles.content}>
          <header className={styles.mobileBar}>
            <button
              type="button"
              className={styles.iconButton}
              onClick={openMobile}
              aria-label="Open navigation"
              aria-expanded={mobileOpen}
            >
              <Icon icon={LayoutAlignLeftIcon} size={20} />
            </button>
            <Link href="/" className={styles.mobileBrand}>
              MorganHacks
            </Link>
          </header>
          <main id="main-content" className={styles.main} tabIndex={-1}>
            {children}
          </main>
        </div>
        <dialog
          ref={mobileDialog}
          className={styles.mobileDialog}
          aria-label="Navigation"
          onClose={() => setMobileOpen(false)}
          onClick={(event) => {
            if (
              event.target === event.currentTarget &&
              event.clientX > event.currentTarget.getBoundingClientRect().right
            )
              closeMobile();
          }}
        >
          <div className={styles.mobileSidebar}>
            <SidebarContents
              sections={navSections}
              identity={sidebar.identity}
              mobile
              onToggle={closeMobile}
              onNavigate={closeMobile}
            />
          </div>
        </dialog>
      </div>
    </SidebarPageContext.Provider>
  );
}
