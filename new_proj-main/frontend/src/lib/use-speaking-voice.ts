"use client";

import { useRef, useState, useSyncExternalStore } from "react";
import { getSpeakingQuestionAudio } from "@/lib/api/ai";

/* ------------------------------ "Read the question aloud on its own" ------------------------------ */

const AUTO_READ_KEY = "educenter.speaking.autoRead";
const AUTO_READ_CHANGED = "educenter:speaking-auto-read-changed";

function readAutoRead(): boolean {
  try {
    return localStorage.getItem(AUTO_READ_KEY) === "1";
  } catch {
    // Private mode or blocked storage: the default is the honest answer.
    return false;
  }
}

function subscribe(onChange: () => void): () => void {
  window.addEventListener(AUTO_READ_CHANGED, onChange);
  window.addEventListener("storage", onChange); // another tab changing it
  return () => {
    window.removeEventListener(AUTO_READ_CHANGED, onChange);
    window.removeEventListener("storage", onChange);
  };
}

/** The student's own preference, remembered on this device. Off until they turn it on: a voice that starts by itself is a surprise. */
export function useAutoRead(): [boolean, (on: boolean) => void] {
  const on = useSyncExternalStore(subscribe, readAutoRead, () => false);

  const set = (next: boolean) => {
    try {
      localStorage.setItem(AUTO_READ_KEY, next ? "1" : "0");
    } catch {
      // Not being able to remember it must not stop it applying to this visit.
    }
    window.dispatchEvent(new Event(AUTO_READ_CHANGED));
  };

  return [on, set];
}

/* ----------------------------------------- The examiner's voice ----------------------------------------- */

/**
 * Plays the examiner's question: the server's recorded voice when one is configured, and otherwise the browser's own speech synthesis,
 * so a centre that never set a voice up still hears its questions. `playing` covers both — the caller keeps the record button off until
 * it is false, so the question never ends up on the student's microphone.
 *
 * A recording is fetched once per question and kept for the visit (replays cost nothing). A browser may refuse to start sound before the
 * student has touched the page; that is not an error to show, the question is on screen either way.
 */
export function useQuestionVoice() {
  const [playing, setPlaying] = useState(false);
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const recordingsRef = useRef(new Map<string, string>());
  const generationRef = useRef(0);

  const halt = () => {
    generationRef.current += 1; // whatever was about to start is now out of date
    const audio = audioRef.current;
    audioRef.current = null;
    audio?.pause();
    audio?.dispatchEvent(new Event("ended")); // a paused recording never ends by itself — let the play() waiting on it finish
    if (typeof window !== "undefined") window.speechSynthesis?.cancel();
  };

  const stop = () => {
    halt();
    setPlaying(false);
  };

  const speakWithBrowser = (text: string) =>
    new Promise<void>((resolve) => {
      if (typeof window === "undefined" || !window.speechSynthesis) return resolve();
      const utterance = new SpeechSynthesisUtterance(text);
      utterance.lang = "en-US";
      utterance.onend = () => resolve();
      utterance.onerror = () => resolve();
      window.speechSynthesis.speak(utterance);
    });

  const play = async (sessionId: string, turnId: string, text: string) => {
    halt();
    const generation = generationRef.current;
    setPlaying(true);

    try {
      let url = recordingsRef.current.get(turnId);
      if (!url) {
        url = URL.createObjectURL(await getSpeakingQuestionAudio(sessionId, turnId));
        recordingsRef.current.set(turnId, url);
      }
      if (generation !== generationRef.current) return; // stopped, or another question started, while it loaded

      await new Promise<void>((resolve, reject) => {
        const audio = new Audio(url);
        audioRef.current = audio;
        audio.onended = () => resolve();
        audio.onerror = () => reject(new Error("The recording could not be played."));
        audio.play().catch(reject);
      });
    } catch {
      // No server voice (503), or the recording would not play: the browser's own.
      if (generation === generationRef.current) await speakWithBrowser(text);
    }

    if (generation === generationRef.current) {
      audioRef.current = null;
      setPlaying(false);
    }
  };

  return { playing, play, stop };
}
