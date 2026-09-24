"use client";

import { createContext, useCallback, useContext, useLayoutEffect, useState } from "react";

type SidebarState = {
  collapsed: boolean;
  toggleCollapsed: () => void;
  collapseSidebar: () => void;
};

const SidebarContext = createContext<SidebarState | null>(null);
const storageKey = "arctic:sidebar-collapsed";
const cookieKey = "arctic_sidebar_collapsed";

export function SidebarStateProvider({ children, initialCollapsed }: {
  children: React.ReactNode;
  initialCollapsed?: boolean;
}) {
  const [collapsed, setCollapsed] = useState(initialCollapsed ?? false);

  useLayoutEffect(() => {
    if (initialCollapsed !== undefined) return;
    try {
      const saved = localStorage.getItem(storageKey) === "true";
      setCollapsed(saved);
      document.cookie = `${cookieKey}=${saved}; Path=/; Max-Age=31536000; SameSite=Lax`;
    } catch {}
  }, [initialCollapsed]);

  const setSidebarCollapsed = useCallback((next: boolean) => {
    setCollapsed(next);
    try {
      localStorage.setItem(storageKey, String(next));
    } catch {}
    document.cookie = `${cookieKey}=${next}; Path=/; Max-Age=31536000; SameSite=Lax`;
  }, []);

  const toggleCollapsed = useCallback(() => setSidebarCollapsed(!collapsed), [collapsed, setSidebarCollapsed]);
  const collapseSidebar = useCallback(() => setSidebarCollapsed(true), [setSidebarCollapsed]);

  return (
    <SidebarContext.Provider value={{ collapsed, toggleCollapsed, collapseSidebar }}>
      {children}
    </SidebarContext.Provider>
  );
}

export function useSidebarState() {
  const state = useContext(SidebarContext);
  if (!state) throw new Error("SidebarStateProvider is required.");
  return state;
}
