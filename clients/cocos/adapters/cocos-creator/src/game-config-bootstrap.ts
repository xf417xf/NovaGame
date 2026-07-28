import type {
  GameConfigClient,
  GameConfigLoadResult,
  GameConfigSnapshot,
} from "@novagame/game-config-client";

type LifecycleClient = Pick<GameConfigClient, "refresh" | "restore">;
type SuccessfulGameConfigLoadResult = Extract<
  GameConfigLoadResult,
  { readonly status: "activated" | "restored" | "revalidated" | "unchanged" }
>;

export type GameConfigBootstrapResult =
  | {
      readonly refresh: GameConfigLoadResult;
      readonly restore: GameConfigLoadResult;
      readonly snapshot: GameConfigSnapshot;
      readonly status: "ready";
    }
  | {
      readonly refresh: GameConfigLoadResult;
      readonly restore: GameConfigLoadResult;
      readonly snapshot: GameConfigSnapshot;
      readonly status: "fallback";
    }
  | {
      readonly refresh: GameConfigLoadResult;
      readonly restore: GameConfigLoadResult;
      readonly snapshot: null;
      readonly status: "unavailable";
    };

export class GameConfigBootstrap {
  private readonly client: LifecycleClient;

  public constructor(client: LifecycleClient) {
    this.client = client;
  }

  public async start(
    options: {
      readonly signal?: AbortSignal | undefined;
    } = {},
  ): Promise<GameConfigBootstrapResult> {
    options.signal?.throwIfAborted();
    const restore = await this.client.restore(options.signal);
    options.signal?.throwIfAborted();
    const refresh = await this.client.refresh(options.signal);
    options.signal?.throwIfAborted();

    if (isSuccessful(refresh)) {
      return {
        refresh,
        restore,
        snapshot: refresh.snapshot,
        status: "ready",
      };
    }

    const snapshot = refresh.snapshot ?? restore.snapshot;
    return snapshot === null
      ? { refresh, restore, snapshot: null, status: "unavailable" }
      : { refresh, restore, snapshot, status: "fallback" };
  }
}

function isSuccessful(result: GameConfigLoadResult): result is SuccessfulGameConfigLoadResult {
  return (
    result.status === "activated" ||
    result.status === "restored" ||
    result.status === "revalidated" ||
    result.status === "unchanged"
  );
}
