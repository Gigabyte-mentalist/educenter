"use client";

import { useEffect, useState } from "react";

/**
 * A value that trails the input by `delay` ms. A search box bound straight to a query key sends one request per keystroke —
 * "ali" is three round trips to the database where one would do — so the query reads this instead of the raw text.
 */
export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}
