/** No 0/O, 1/l/I: a password handed to a student is usually read out loud or copied off a screen. */
const ALPHABET = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

/** A random password from `crypto`, 10 characters by default — above the API's 8-character minimum. */
export function generatePassword(length = 10): string {
  const values = crypto.getRandomValues(new Uint32Array(length));
  return Array.from(values, (n) => ALPHABET[n % ALPHABET.length]).join("");
}
