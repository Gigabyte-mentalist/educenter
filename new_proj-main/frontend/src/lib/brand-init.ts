// Deliberately not a "use client" module: the root layout is a server component and inlines this script as a string, and
// a string exported from a client module reaches a server component as a reference, not as text.

export const BRAND_VARS_KEY = "educenter:org-brand-vars";

/**
 * Runs before React hydrates, inlined in <head> next to the theme script. The brand colour is applied as custom properties on the
 * root element (see brand.ts); without this the page would paint in the product's green and then change to the centre's colour
 * once the organization has loaded. It applies what the last visit stored, which is right nearly every time.
 */
export const BRAND_INIT_SCRIPT = `(function(){try{
var v=JSON.parse(localStorage.getItem(${JSON.stringify(BRAND_VARS_KEY)})||"null");
if(v)for(var k in v)document.documentElement.style.setProperty(k,v[k]);
}catch(e){}})();`;
