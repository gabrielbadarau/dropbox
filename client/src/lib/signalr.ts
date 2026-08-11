import { useEffect, useRef } from "react";
import * as signalR from "@microsoft/signalr";
import { getToken } from "./api";
import type { ChangeEventSummary } from "./types";

const HUB_URL = import.meta.env.VITE_HUB_URL as string;

// Real-time push is additive on top of polling (GET /files/changes),
// exactly as the reference design intends - if the hub connection never
// comes up, or drops and can't reconnect, the app still works correctly,
// just without live updates.
export function useChangesHub(
  onChange: (event: ChangeEventSummary) => void,
  enabled: boolean,
) {
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    if (!enabled) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect()
      .build();

    connection.on("ChangeOccurred", (event: ChangeEventSummary) => {
      onChangeRef.current(event);
    });

    connection.start().catch(() => {
      // Swallowed deliberately - see comment above.
    });

    return () => {
      connection.stop();
    };
  }, [enabled]);
}
