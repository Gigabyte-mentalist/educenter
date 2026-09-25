"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";

export type Theme = "light" | "dark" | "system";

export const THEME_STORAGE_KEY = "educenter:theme";

/**
 * Runs before React hydrates, inlined in <head>. Without it the page paints with the light tokens and then
 * swaps — a white flash on every navigation for anyone using night mode, which is precisely the thing night
 * mode exists to avoid. It is a string rather than a function because it has to be serialised into the HTML.
 */
export const THEME_INIT_SCRIPT = `(function(){try{
var s=localStorage.getItem(${JSON.stringify(THEME_STORAGE_KEY)})||"system";
var d=s==="dark"||(s==="system"&&window.matchMedia("(prefers-color-scheme: dark)").matches);
document.documentElement.setAttribute("data-theme",d?"dark":"light");
}catch(e){}})();`;

const THEME_CHANGED_EVENT = "educenter:theme-changed";

function systemPrefersDark(): boolean {
  return typeof window !== "undefined" && window.matchMedia("(prefers-color-scheme: dark)").matches;
}

function read(): Theme {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "system") return stored;
  } catch {
    // Private mode or blocked storage: the default is the honest answer.
  }
  return "system";
}

function apply(theme: Theme): void {
  const dark = theme === "dark" || (theme === "system" && systemPrefersDark());
  document.documentElement.setAttribute("data-theme", dark ? "dark" : "light");
}

// localStorage is the store, so the hook reads it through useSyncExternalStore rather than copying it into
// state in an effect: that copy renders once with the wrong value and then again with the right one, and on
// this particular value the wrong render is a flash of the other theme.
function subscribe(onChange: () => void): () => void {
  window.addEventListener(THEME_CHANGED_EVENT, onChange);
  window.addEventListener("storage", onChange); // another tab switching theme
  return () => {
    window.removeEventListener(THEME_CHANGED_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

export function setStoredTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
    // Not being able to remember the choice must not stop it applying to this session.
  }
  apply(theme);
  window.dispatchEvent(new Event(THEME_CHANGED_EVENT));
}

export function useTheme() {
  // The server snapshot is "system": the server cannot know, and the markup has to match what React produces
  // on the first client render. The paint is already correct regardless — THEME_INIT_SCRIPT set the attribute.
  const theme = useSyncExternalStore<Theme>(subscribe, read, () => "system");
  const hydrated = useSyncExternalStore(subscribe, () => true, () => false);

  // While on "system", the OS switching at sunset has to move the app with it.
  useEffect(() => {
    if (theme !== "system") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => apply("system");
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, [theme]);

  const setTheme = useCallback((next: Theme) => setStoredTheme(next), []);

  const resolved: "light" | "dark" =
    theme === "system" ? (hydrated && systemPrefersDark() ? "dark" : "light") : theme;

  return { theme, resolved, setTheme, hydrated };
}
