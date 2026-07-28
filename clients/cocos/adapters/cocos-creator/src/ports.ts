export interface FetchHeaders {
  get(name: string): null | string;
}

export interface FetchResponse {
  readonly headers: FetchHeaders;
  readonly status: number;
  arrayBuffer(): Promise<ArrayBuffer>;
}

export interface FetchRequestInit {
  readonly headers?: Readonly<Record<string, string>>;
  readonly method?: string;
  readonly signal?: AbortSignal | undefined;
}

export type FetchLike = (input: string, init?: FetchRequestInit) => Promise<FetchResponse>;

export interface StorageLike {
  getItem(key: string): null | string;
  removeItem(key: string): void;
  setItem(key: string, value: string): void;
}
